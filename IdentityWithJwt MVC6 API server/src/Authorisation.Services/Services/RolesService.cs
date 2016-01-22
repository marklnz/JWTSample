using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Services
{
    public static class Roles
    {
        private static readonly string[] roles = { "Admin" };

        public static string Admin { get { return roles[0]; } }

        public static IEnumerable<string> All { get { return roles; } }
    }
}
