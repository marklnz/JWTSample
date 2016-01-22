using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Web
{
    public class TokenAuthOptions
    {
        public string Issuer { get; set; }
        public SigningCredentials SigningCredentials { get; set; }
        public int AccessTokenValidLifetime { get; set; }

    }
}
