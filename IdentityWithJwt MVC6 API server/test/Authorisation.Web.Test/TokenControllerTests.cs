using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Authorisation.Web.Controllers;
using Authorisation.Domain;
using Authorisation.Web;
using Moq;
using System.Security.Cryptography;
using System.IdentityModel.Tokens;
using Authorisation.Services;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Http.Internal;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Text;
using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Authentication;
using Microsoft.AspNet.Identity;
using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Builder.Internal;
using Microsoft.AspNet.Builder;
using Authorisation.Services.Utilities;
using Microsoft.Data.Entity;
using Authorisation.Web.Test.Mocks;
using Microsoft.Extensions.Configuration;

namespace Authorisation.Test
{
    public class TokenControllerTests
    {
        private TokenController tokenController;
        private string ApiClientKey = "UNITTESTSECRET";
        private ApplicationBuilder app;

        public TokenControllerTests()
        {
            // Mock the HttpContext and authentication level things so we don't need the full web stack
            var context = new Mock<HttpContext>();
            var auth = new Mock<AuthenticationManager>();

            //TODO: It would be ideal if we were to wrap this up in a fake HttpContext so that we could then use the same context both here and in the tests (created via the private CreateContext method)
            context.Setup(c => c.Authentication).Returns(auth.Object).Verifiable();
            auth.Setup(a => a.SignInAsync(new IdentityCookieOptions().ApplicationCookieAuthenticationScheme,
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<AuthenticationProperties>())).Returns(Task.FromResult(0)).Verifiable();

            var contextAccessor = new Mock<IHttpContextAccessor>();
            contextAccessor.Setup(a => a.HttpContext).Returns(context.Object);
            var services = new ServiceCollection();
            services.AddLogging();

            //TODO: AddInstance has been renamed to AddSingleton, as of the nightly builds on 27 November, so this will have to change when we go to using RC2 or RTM
            services.AddInstance(contextAccessor.Object);

            // Configure the options system and add our Domain.IdentityOptions instance to it, using the test ApiClientKey
            services.AddOptions();
            services.Configure<Domain.IdentityOptions>(opt => opt.ApiClientKey = this.ApiClientKey);

            // Add identity services and set up to use an in-memory EF database for the authorisation data storage
            services.AddIdentity<ApplicationUser, IdentityRole>()
                .AddEntityFrameworkStores<AuthorisationDbContext>()
                .AddDefaultTokenProviders();

            services.AddSingleton<IUserStore<ApplicationUser>, InMemoryUserStore<ApplicationUser>>();
            services.AddSingleton<IRoleStore<IdentityRole>, InMemoryRoleStore<IdentityRole>>();

            // Create in-memory database, and the auth datacontext
            services.AddEntityFramework()
                    .AddInMemoryDatabase()
                    .AddDbContext<AuthorisationDbContext>(options =>
                        options.UseInMemoryDatabase());

            // Add our Authentication query and command services to the DI container as transients
            services.AddTransient<AuthenticationQueryService>();
            services.AddTransient<AuthenticationCmdService>();

            // Set up the app builder to complete the DI setup.
            app = new ApplicationBuilder(services.BuildServiceProvider());

            // Set up test user
            app.ApplicationServices.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(new ApplicationUser() { UserName = "TestUser" }, "TestPassword_1");

            // Calculate hash for secret for ApiClients
            HashAlgorithm hashAlgorithm = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(ApiClientKey));
            byte[] secretByteValue = System.Text.Encoding.UTF8.GetBytes("unittestSecret1");
            string secretHash = System.Text.Encoding.UTF8.GetString(hashAlgorithm.ComputeHash(secretByteValue));

            // Add ApiClient records to database and save changes
            app.ApplicationServices.GetRequiredService<AuthorisationDbContext>().Add(new ApiClient() { Id = 1, Active = true, ApplicationType = ApplicationTypes.JavaScript, Name = "UnitTestClient", RefreshTokenLifeTime = 1440, Secret = secretHash });
            app.ApplicationServices.GetRequiredService<AuthorisationDbContext>().Add(new ApiClient() { Id = 2, Active = true, ApplicationType = ApplicationTypes.NativeConfidential, Name = "ConfidentialUnitTestClient", RefreshTokenLifeTime = 1440, Secret = secretHash });
            app.ApplicationServices.GetRequiredService<AuthorisationDbContext>().SaveChanges();

            // Generate a random RSA key for testing. This is only ok for testing!
            RSAParameters keyParams = RSAKeyUtils.GetRandomKey();

            // Create the key, and a set of token options to record signing credentials
            // using that key, along with the other parameters we will need in the
            // token controlller.
            RsaSecurityKey key = new RsaSecurityKey(keyParams);
            TokenAuthOptions tokenOptions = new TokenAuthOptions()
            {
                Issuer = "test issuer",
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256Signature),
                AccessTokenValidLifetime = 5
            };

            // Finally, create an instance of the controller for testing with
            tokenController = new TokenController(app.ApplicationServices.GetRequiredService<UserManager<ApplicationUser>>(),
                                                  app.ApplicationServices.GetRequiredService<SignInManager<ApplicationUser>>(),
                                                  tokenOptions, app.ApplicationServices.GetRequiredService<AuthenticationQueryService>(),
                                                  app.ApplicationServices.GetRequiredService<AuthenticationCmdService>());
        }

        [Fact]
        public async Task Logon_ValidUserAndClientCredentials_ReturnsGoodResponse()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForQuery), response.StatusCode);

            // Test that the response contains the access token, the refresh token and the access token's expiry time.
            Assert.NotNull(authResponse);
            Assert.Equal(true, (bool)authResponse["authenticated"]);
            Assert.Equal(5, (int)authResponse["tokenExpires"]);
            Assert.Equal(1, (int)authResponse["entityId"]);
            Assert.NotNull((string)authResponse["refreshtoken"]);
            Assert.NotNull((string)authResponse["accesstoken"]);
        }

        [Fact]
        public async Task Logon_InvalidUserAndValidClientCredentials_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"NonExistantUser\",\"password\":\"WrongPassword_2\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_ValidUserWithWrongPasswordAndValidClientCredentials_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"WrongPassword_2\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_NoUserName_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_NoPassword_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_NoApiClientId_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_NoClientSecret_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_ConfidentialApiClientWithIncorrectClientSecret_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"2\",\"clientsecret\":\"WrongunittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task Logon_ConfidentialApiClientWithCorrectClientSecret_ReturnsValidResponse()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"2\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForQuery), response.StatusCode);

            // Test that the response contains the access token, the refresh token and the access token's expiry time.
            Assert.NotNull(authResponse);
            Assert.Equal(true, (bool)authResponse["authenticated"]);
            Assert.Equal(5, (int)authResponse["tokenExpires"]);
            Assert.Equal(1, (int)authResponse["entityId"]);
            Assert.NotNull((string)authResponse["refreshtoken"]);
            Assert.NotNull((string)authResponse["accesstoken"]);
        }

        [Fact]
        public async Task Logon_NonExistantApiClient_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"5\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);

            // Test that the response contains nothing
            Assert.Null(authResponse);
        }

        [Fact]
        public async Task LogOff_MissingRefreshToken_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"apiclientid\":\"1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_MissingApiClientId_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"refreshtoken\":\"UxIjFQlXZCq7imsUSCb2+6v7HU39g3+9HXuF2dS6tHg=\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_MissingUsername_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"apiclientid\":\"1\",\"refreshtoken\":\"UxIjFQlXZCq7imsUSCb2+6v7HU39g3+9HXuF2dS6tHg=\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_UnknownRefreshToken_Returns500Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();

            requestBody = "{\"apiclientid\":\"1\",\"username\":\"TestUser\",\"refreshtoken\":\"UxIjFQlXZCq7imsUSCb2+6v7HU39g3+9HXuF2dS6tHg=\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 401
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_UnknownApiClientId_Returns500Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"5\",\"username\":\"TestUser\",\"refreshtoken\":\"", token,"\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 500
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_UnknownUsername_Returns500Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"1\",\"username\":\"UnknownTestUser\",\"refreshtoken\":\"", token, "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 500
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_MismatchedTokenClientIdAndUsername_Returns500Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"2\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"1\",\"username\":\"TestUser\",\"refreshtoken\":\"", token, "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 500
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError), response.StatusCode);
        }

        [Fact]
        public async Task LogOff_MatchingTokenClientIdAndUsername_Returns204Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"1\",\"username\":\"TestUser\",\"refreshtoken\":\"", token, "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            await tokenController.LogOff();

            // Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;

            // Status code should be 500
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForCommand), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_MissingRefreshToken_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"1\",\"username\":\"TestUser\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_MissingApiClientId_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"TestUser\",\"refreshtoken\":\"", token, "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_MissingUsername_Returns400Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"apiclientid\":\"1\",\"refreshtoken\":\"", token, "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.BadRequest), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_UnknownRefreshToken_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"1\",\"refreshtoken\":\"UxIjFQlXZCq7imsUSCb2+6v7HU39g3+9HXuF2dS6tHg=\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_UnknownApiClientId_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"5\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_UnknownUsername_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"UnknownTestUser\",\"apiclientid\":\"1\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_MismatchedApiClientIdUsernameAndRefreshToken_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"2\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_MatchingUsernameApiClientIdAndRefreshToken_ReturnsAccessTokenAnd204Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"1\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();
            authResponse = (JObject)result.Value;

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForQuery), response.StatusCode);

            // Test that the response contains the access token, the refresh token and the access token's expiry time.
            Assert.NotNull(authResponse);
            Assert.Equal(true, (bool)authResponse["authenticated"]);
            Assert.Equal(5, (int)authResponse["tokenExpires"]);
            Assert.Equal(1, (int)authResponse["entityId"]);
            Assert.NotNull((string)authResponse["accesstoken"]);
        }

        [Fact]
        public async Task GetAccessToken_ExpiredRefreshToken_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            // Force the refresh token to be expired
            var context = app.ApplicationServices.GetRequiredService<AuthorisationDbContext>();
            context.RefreshTokens.Single().IssuedUtc = DateTime.UtcNow.AddDays(-3);
            context.RefreshTokens.Single().ExpiresUtc = DateTime.UtcNow.AddDays(-2);
            await context.SaveChangesAsync();

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"1\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        [Fact]
        public async Task GetAccessToken_InactiveApiClient_Returns401Response()
        {
            // Arrange
            tokenController.ActionContext = CreateContext();
            string requestBody = "{\"username\":\"TestUser\",\"password\":\"TestPassword_1\",\"rememberme\":false,\"apiclientid\":\"1\",\"clientsecret\":\"unittestSecret1\"}";
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            JsonResult result = await tokenController.Logon();
            JObject authResponse = (JObject)result.Value;

            var token = (string)authResponse["refreshtoken"];

            // Now ensure that the api client is deactivated - this is different to having the token revoked. In this case the entire client api is marked as inactive. We
            // need to be sure that we reject tokens in this state in case the refreshtoken itself is not removed for whatever reason. Defence in depth.
            var context = app.ApplicationServices.GetRequiredService<AuthorisationDbContext>();
            context.ApiClients.Single(ac => ac.Id == 1).Active = false;
            await context.SaveChangesAsync();

            requestBody = string.Concat("{\"username\":\"TestUser\",\"apiclientid\":\"1\",\"refreshtoken\":\"" + token + "\"}");
            tokenController.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(requestBody));

            // Act
            result = await tokenController.GetAccessToken();

            //Assert
            // Get the response body so we can check the status code
            var response = tokenController.Response;
            Assert.Equal(CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated), response.StatusCode);
        }

        private ActionContext CreateContext()
        {
            var actionContext = new ActionContext();
            actionContext.HttpContext = new DefaultHttpContext();

            // Mock user details and route data go here
            return actionContext;
        }
    }
}
