using Authorisation.Domain;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.Data.Entity;
using Microsoft.Data.Entity.Infrastructure;
using Microsoft.Data.Entity.Migrations;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Services
{
    public static class Extensions
    {
        public static void EnsureRolesCreated(this IApplicationBuilder app)
        {
            var context = app.ApplicationServices.GetService<AuthorisationDbContext>();
            if (context.AllMigrationsApplied())
            {
                var roleManager = app.ApplicationServices.GetService<RoleManager<IdentityRole>>();
                foreach (var role in Roles.All)
                {
                    if (!roleManager.RoleExistsAsync(role.ToUpper()).Result)
                    {
                        roleManager.CreateAsync(new IdentityRole { Name = role });
                    }
                }
            }
        }
        public static void EnsureTestUserCreated(this IApplicationBuilder app)
        {
            var context = app.ApplicationServices.GetService<AuthorisationDbContext>();
            if (context.AllMigrationsApplied())
            {
                var userManager = app.ApplicationServices.GetService<UserManager<ApplicationUser>>();

                Task<ApplicationUser> userSearch = new Task<ApplicationUser>(() => { return userManager.FindByNameAsync("TestUser").Result; });
                userSearch.RunSynchronously();

                // if the user was not found, create it
                if (userSearch.Result == null)
                {
                    ApplicationUser user = new ApplicationUser() { Email = "test@eyede.co.nz", FirstName = "Test", LastName = "User", UserName = "TestUser" };
                    Task<IdentityResult> createUser = Task.Run(() => userManager.CreateAsync(user, "TestPassword_1"));
                }
            }
        }

        public static bool AllMigrationsApplied(this DbContext context)
        {
            var applied = context.GetService<IHistoryRepository>()
                .GetAppliedMigrations()
                .Select(m => m.MigrationId);

            var total = context.GetService<IMigrationsAssembly>()
                .Migrations
                .Select(m => m.Key);

            return !total.Except(applied).Any();
        }
    }
}
