using Authorisation.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Services
{
    public class AuthenticationCmdService
    {
        private AuthorisationDbContext dc;

        public AuthenticationCmdService() : this(new AuthorisationDbContext())
        {
        }

        public AuthenticationCmdService(AuthorisationDbContext dc)
        {
            this.dc = dc;
        }

        public async Task<bool> AddRefreshToken(RefreshToken token)
        {

            var existingToken = dc.RefreshTokens.Where(r => r.Subject == token.Subject && r.ClientId == token.ClientId).SingleOrDefault();

            if (existingToken != null)
            {
                var result = await RemoveRefreshToken(existingToken);
            }

            dc.RefreshTokens.Add(token);

            return await dc.SaveChangesAsync() > 0;
        }

        public async Task<bool> RemoveRefreshToken(string refreshTokenId)
        {
            var refreshToken = dc.RefreshTokens.Where(r => r.Id == refreshTokenId).Single();

            if (refreshToken != null)
            {
                dc.RefreshTokens.Remove(refreshToken);
                return await dc.SaveChangesAsync() > 0;
            }

            return false;
        }

        public async Task<bool> RemoveRefreshToken(RefreshToken refreshToken)
        {
            dc.RefreshTokens.Remove(refreshToken);
            return await dc.SaveChangesAsync() > 0;
        }



    }
}
