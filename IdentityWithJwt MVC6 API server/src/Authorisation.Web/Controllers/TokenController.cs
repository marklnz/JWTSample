using Authorisation.Domain;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Mvc;
using Newtonsoft.Json.Linq;
using Authorisation.Services;
using Authorisation.Services.Utilities;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.AspNet.Cors;

namespace Authorisation.Web.Controllers
{
    [Route("api/[controller]")]
    [Authorize("Bearer")]
    public class TokenController : Controller
    {
        private readonly UserManager<ApplicationUser> userManager;
        private readonly SignInManager<ApplicationUser> signInManager;
        private readonly TokenAuthOptions tokenOptions;
        private readonly AuthenticationQueryService authenticationQueryService;
        private readonly AuthenticationCmdService authenticationCmdService;

        public TokenController(UserManager<ApplicationUser> UserManager, SignInManager<ApplicationUser> SignInManager, TokenAuthOptions TokenOptions, AuthenticationQueryService AuthenticationQueryService, AuthenticationCmdService AuthenticationCmdService)
        {
            userManager = UserManager;
            signInManager = SignInManager;
            tokenOptions = TokenOptions;
            authenticationQueryService = AuthenticationQueryService;
            authenticationCmdService = AuthenticationCmdService;
        }

        //
        // POST Token/Logon/
        /// <summary>
        /// The Logon action authenticates the user, and the client application they're using. If these authentication checks pass, a long-lived Refresh Token is created along with an initial resource Access Token.
        /// The Refresh token is saved to the database and associated with the ApiClient record that the calling application has authenticated as, and then both tokens are returned to the caller.
        /// </summary>
        /// <returns>a json object that contains the generated Refresh and Access Tokens.</returns>
        [HttpPost]
        [AllowAnonymous]
        [Route("[action]")]
        public async Task<JsonResult> Logon()
        {
            // Declare the response object, set to null by default - will be populated later if we authenticate successfully
            JObject authResponse = null;

            // Default to returning 404
            ResultType result = ResultType.InternalServerError;

            string refreshToken = null;
            string accessToken = null;
            try
            {
                //TODO: Build and use a model object to hold the incoming data ?
                string data = await ReadBody();
                var json = JObject.Parse(data);

                string username = (string)json["username"];
                string password = (string)json["password"];
                //bool rememberMe = (bool)json["rememberme"];
                bool rememberMe = false;

                int clientId = (int)(json["apiclientid"] ?? 0);
                string clientSecret = (string)json["clientsecret"];

                // Validate that we have all we need here - return Bad Request (400) if we don't
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || clientId <= 0 || string.IsNullOrWhiteSpace(clientSecret))
                    result = ResultType.BadRequest;
                else
                {
                    var user = await userManager.FindByNameAsync(username);
                    if (user == null)
                    {
                        result = ResultType.Unauthenticated;
                    }
                    else
                    {
                        var signinResult = await signInManager.PasswordSignInAsync(username, password, rememberMe, lockoutOnFailure: true);
                        if (signinResult.Succeeded)
                        {
                            HttpContext.User = await signInManager.CreateUserPrincipalAsync(user);

                            // Authenticate the api client using the provided credentials
                            if (await authenticationQueryService.AuthenticateClient(clientId, clientSecret))
                            {
                                // Generate a refresh token
                                refreshToken = await GetRefreshToken(clientId);

                                // Now generate a short-lived access token
                                // Grab the api client's name to use as the audience
                                var queryResult = await authenticationQueryService.FindClient(clientId);
                                if (queryResult.ResultType != ResultType.OkForQuery)
                                    throw new SecurityTokenException("API Client authenticated successfully but the related ApiClient record could not be retrieved from the database.");

                                accessToken = await BuildAccessToken(tokenOptions.AccessTokenValidLifetime, queryResult.Content.Name);

                                // Now build the response and return 200 status code
                                authResponse = JObject.FromObject(new { authenticated = true, entityId = 1, accesstoken = accessToken, tokenExpires = tokenOptions.AccessTokenValidLifetime, refreshtoken = refreshToken });

                                result = ResultType.OkForQuery;
                            }
                            else
                            {
                                result = ResultType.Unauthenticated;
                            }
                        }
                        else
                        {
                            result = ResultType.Unauthenticated;
                        }
                    }
                }
            }
            catch
            {
                // If any exceptions are thrown, we need to ensure that we tidy things up and then re-throw. We shouldn't be leaving things in an inconsistent state.
                if (string.IsNullOrWhiteSpace(refreshToken) == false)
                {
                    bool success = await authenticationCmdService.RemoveRefreshToken(refreshToken);

                    if (success && User.Identity.IsAuthenticated)
                    {
                        // Sign the user out using the signin manager
                        await signInManager.SignOutAsync();
                    }
                }

                throw;
            }

            Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(result);

            if (result == ResultType.OkForQuery)
                return Json(authResponse);
            else
                return Json(null);
        }


        //
        // POST Token/GetAccessToken
        /// <summary>
        /// When the client application detects that an access token has expired, or is just about to expire, it will need to call this method to generate a new one. This action expects that the body of the request includes
        /// a refresh token that is valid, and has been issued to the calling aplication, for the given user. The body should also include the user's username, and the apiclientid of the calling application.
        /// </summary>
        /// <returns>returns a short-lived Access Token</returns>
        [HttpPost]
        [AllowAnonymous]
        [Route("[action]")]
        public async Task<JsonResult> GetAccessToken()
        {
            // Declare the response object, set to null by default - will be populated later if we authenticate successfully
            JObject authResponse = null;

            // Default to returning 500
            ResultType result = ResultType.InternalServerError;

            // Receive and Validate the RefreshToken
            string data = await ReadBody();
            var json = JObject.Parse(data);

            string username = (string)json["username"];
            int clientId = (int)(json["apiclientid"] ?? 0);
            string refreshTokenId = (string)json["refreshtoken"];

            // Validate that we have all we need here - return Bad Request (400) if we don't
            if (string.IsNullOrWhiteSpace(username) || clientId <= 0 || string.IsNullOrWhiteSpace(refreshTokenId))
                result = ResultType.BadRequest;
            else
            {
                // Grab the api client's name to use as the audience for the access token
                var queryResult = await authenticationQueryService.FindClient(clientId);
                if (queryResult.ResultType != ResultType.OkForQuery)
                    result = ResultType.Unauthenticated;
                else
                {
                    if (await ValidateRefreshToken(refreshTokenId, clientId, username))
                    {
                        HttpContext.User = await signInManager.CreateUserPrincipalAsync(await userManager.FindByNameAsync(username));

                        var accessToken = await BuildAccessToken(tokenOptions.AccessTokenValidLifetime, queryResult.Content.Name);

                        authResponse = JObject.FromObject(new { authenticated = true, entityId = 1, accesstoken = accessToken, tokenExpires = tokenOptions.AccessTokenValidLifetime });
                        result = ResultType.OkForQuery;
                    }
                    else
                    {
                        result = ResultType.Unauthenticated;
                    }
                }
            }

            Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(result);

            if (result == ResultType.OkForQuery)
                return Json(authResponse);
            else
                return Json("{\"unauthenticated\"=\"true\"}");
        }

        //
        // POST: Token/LogOff/
        /// <summary>
        /// The user will be signed out and any refresh tokens for them that have been issued for use with the calling client application will be revoked.
        /// </summary>
        /// <returns>A response status code indicating success.</returns>
        [HttpPost]
        [AllowAnonymous]
        [Route("[action]")]
        public async Task LogOff()
        {
            // Retrieve the calling application's apiclientid
            string data = await ReadBody();
            var json = JObject.Parse(data);

            string refreshTokenId = (string)json["refreshtoken"];
            int clientId = (int)(json["apiclientid"] ?? 0);
            string username = (string)json["username"];

            // Validate that we have all we need here - return Bad Request (400) if we don't
            if (string.IsNullOrWhiteSpace(username) || clientId <= 0 || string.IsNullOrWhiteSpace(refreshTokenId))
                Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest);
            else
            {
                // Confirm that this token is for this user, with this client
                if (await ValidateRefreshToken(refreshTokenId, clientId, username))
                {
                    bool success = await authenticationCmdService.RemoveRefreshToken(refreshTokenId);

                    if (success)
                    {
                        // Sign the user out using the signin manager
                        await signInManager.SignOutAsync();
                        Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForCommand);
                    }
                    else
                        Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError);
                }
                else
                    Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError);
            }
        }

        private async Task<string> ReadBody()
        {
            using (var reader = new StreamReader(Request.Body))
            {
                return await reader.ReadToEndAsync();
            }
        }

        private async Task<string> GetRefreshToken(int clientId)
        {
            // Retrieve the client Application record
            ApiClient client = null;

            // Retrieve the client
            QueryServiceResult<ApiClient> clientSearchResult = await authenticationQueryService.FindClient(clientId);
            client = clientSearchResult.ResultType == ResultType.OkForQuery ? clientSearchResult.Content : null;

            // If we found the user, then create a token, and save it against the client, else we throw exception
            if (client == null)
            {
                throw new SecurityTokenException("Could not generate refresh token. No ApiClient record exists with this Id.");
            }
            else
            {
                var refreshTokenId = Guid.NewGuid().ToString("n");
                int refreshTokenLifeTime = client.RefreshTokenLifeTime;

                HashAlgorithm hashAlgorithm = new System.Security.Cryptography.HMACSHA256();
                byte[] byteValue = System.Text.Encoding.UTF8.GetBytes(refreshTokenId);
                byte[] byteHash = hashAlgorithm.ComputeHash(byteValue);

                // Creating a refresh token for this client
                var refreshToken = new RefreshToken
                {
                    Id = Convert.ToBase64String(byteHash),
                    ClientId = clientId,
                    Subject = User.GetUserName(),
                    IssuedUtc = DateTime.UtcNow,
                    ExpiresUtc = DateTime.UtcNow.AddMinutes(Convert.ToDouble(refreshTokenLifeTime))
                };

                // Save the token to the database, against the client
                if (await authenticationCmdService.AddRefreshToken(refreshToken))
                    // Return the token id to the caller
                    return refreshToken.Id;
                else
                    throw new SecurityTokenException("There was an error saving refresh token to database");
            }
        }

        private async Task<bool> ValidateRefreshToken(string refreshTokenId, int clientId, string username)
        {
            bool validationResult = false;

            // Check that the token can be found - if not then it's either been revoked or this is a bogus Token ID
            var tokenResult = await authenticationQueryService.FindRefreshToken(refreshTokenId);
            RefreshToken tokenData = null;

            if (tokenResult.ResultType == ResultType.OkForQuery)
                tokenData = tokenResult.Content;

            if (tokenData != null)
            {
                var clientResult = await authenticationQueryService.FindClient(clientId);
                ApiClient client = null;
                if (clientResult.ResultType == ResultType.OkForQuery)
                {
                    client = clientResult.Content;

                    // Check that the apiclient that sent it is active
                    // Check that the client that sent it is the same as the one that it was created for
                    // Check that the user that sent it is the same as the one that it was created for
                    // Check that the token is not expired!
                    if (client.Active == false || tokenData.ClientId != clientId || tokenData.Subject.ToLowerInvariant() != username.ToLowerInvariant() || tokenData.ExpiresUtc < DateTime.UtcNow)
                        validationResult = false;
                    else
                        validationResult = true;
                }
                else
                    validationResult = false;
            }
            else
                // Token either was never issued, or it has been revoked (deleted from database)
                validationResult = false;

            return validationResult;
        }

        private async Task<String> BuildAccessToken(int ValidLifetime, string TokenAudience)
        {
            var handler = new JwtSecurityTokenHandler();

            // Grab the user
            string username = User.GetUserName();
            var user = await this.userManager.FindByNameAsync(username);

            // If we found the user, then create a token, else we throw exception
            if (user != null)
            {
                // Creating a simple claims identity to use in the token
                //TODO: Populate claims - at the moment we only add the database userid as a claim named "EntityID". The framework adds a "unique_name" claim containing the user's username. We will need
                //TODO: to add at a minimum the user's roles, or their specific access rights, depending on the system being built.
                ClaimsIdentity identity = new ClaimsIdentity(new GenericIdentity(username, "TokenAuth"), new[] { new Claim("EntityID", user.Id, ClaimValueTypes.String) });

                // Build token with a short expiry - 5 minutes
                var securityToken = handler.CreateToken(
                    issuer: tokenOptions.Issuer,
                    audience: TokenAudience,
                    signingCredentials: tokenOptions.SigningCredentials,
                    subject: identity,
                    expires: DateTime.UtcNow.AddMinutes(ValidLifetime)
                    );

                return handler.WriteToken(securityToken);
            }
            else
            {
                throw new SecurityTokenException();
            }
        }

    }
}
