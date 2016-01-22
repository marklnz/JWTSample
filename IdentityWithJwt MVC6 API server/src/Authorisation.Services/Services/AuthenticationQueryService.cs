using Authorisation.Domain;
using Authorisation.Services.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.OptionsModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Authorisation.Services
{
    public class AuthenticationQueryService
    {
        private AuthorisationDbContext dc;
        private IdentityOptions options;

        public AuthenticationQueryService(IOptions<IdentityOptions> options) : this(new AuthorisationDbContext(), options)
        {

        }

        public AuthenticationQueryService(AuthorisationDbContext dc, IOptions<IdentityOptions> options)
        {
            this.dc = dc;
            this.options = options.Value;
        }

        public virtual async Task<QueryServiceResultList<ApplicationUser>> GetUsers()
        {
            return await QueryServiceResultList<ApplicationUser>.Create(dc.Users, ResultType.OkForQuery);
        }

        public virtual async Task<QueryServiceResult<ApplicationUser>> FindByIdAsync(string id)
        {
            return await QueryServiceResult<ApplicationUser>.GetFirstOfSet(dc.Users, u => u.Id == id);
        }

        public virtual async Task<QueryServiceResult<ApplicationUser>> FindByNameAsync(string username)
        {
            return await QueryServiceResult<ApplicationUser>.GetFirstOfSet(dc.Users, u => u.UserName == username);
        }

        public virtual async Task<QueryServiceResult<ApiClient>> FindClient(int clientId)
        {
            return await QueryServiceResult<ApiClient>.GetFirstOfSet(dc.ApiClients, c => c.Id == clientId);
        }

        public virtual async Task<QueryServiceResult<RefreshToken>> FindRefreshToken(string refreshTokenId)
        {
            return await QueryServiceResult<RefreshToken>.GetFirstOfSet(dc.RefreshTokens, c => c.Id == refreshTokenId);
        }

        public virtual async Task<QueryServiceResultList<RefreshToken>> GetAllRefreshTokens()
        {
            return await QueryServiceResultList<RefreshToken>.Create(dc.RefreshTokens, ResultType.OkForQuery);
        }

        // Simply returns true or false. Returns true if secret provided matches the stored secret for the given clientid and client is active.
        // Returns false if either clientid or clientsecret are not provided, client is not in the database, the client is not active, or the client is a confidential one and the secret does not match
        public virtual async Task<bool> AuthenticateClient(int ClientId, string ClientSecret)
        {
            bool returnValue = false;
            ApiClient client = null;
            string clientSecretKey = options.ApiClientKey;

            if (ClientId != 0 & ClientSecret != null)
            {
                QueryServiceResult<ApiClient> searchResult = await this.FindClient(ClientId);
                if (searchResult.ResultType == ResultType.OkForQuery)
                    client = searchResult.Content;

                if (client != null)
                {
                    if (client.Active)
                    {
                        if (client.ApplicationType == ApplicationTypes.NativeConfidential)
                        {
                            if (string.IsNullOrWhiteSpace(ClientSecret) == false)
                            {
                                HashAlgorithm hashAlgorithm = new System.Security.Cryptography.HMACSHA256(System.Text.Encoding.UTF8.GetBytes(options.ApiClientKey));
                                byte[] secretByteValue = System.Text.Encoding.UTF8.GetBytes(ClientSecret);
                                byte[] secretHash = hashAlgorithm.ComputeHash(secretByteValue);

                                if (client.Secret == System.Text.Encoding.UTF8.GetString(secretHash))
                                {
                                    returnValue = true;
                                }
                            }
                        }
                        else
                        {
                            returnValue = true;
                        }
                    }
                }
            }

            return returnValue;
        }
    }
}
