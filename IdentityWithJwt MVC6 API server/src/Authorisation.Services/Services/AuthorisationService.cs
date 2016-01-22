using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Services
{
    public class AuthorisationService
    {
        private AuthorisationDbContext dc;

        public AuthorisationService() : this(new AuthorisationDbContext())
        {

        }

        public AuthorisationService(AuthorisationDbContext dc)
        {
            this.dc = dc;
        }
    }
}
