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
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace Authorisation.Web.Controllers
{
    [Route("api/[controller]")]
    [Authorize("Bearer")]
    public class AccountController : Controller
    {
        private readonly UserManager<ApplicationUser> userManager;

        public AccountController(UserManager<ApplicationUser> UserManager)
        {
            userManager = UserManager;
        }

        //
        // GET: Account/
        [HttpGet]
        [AllowAnonymous]
        public async Task<IEnumerable<Domain.ApplicationUser>> GetUsers()
        {
            var userList = this.userManager.Users.AsEnumerable();

            Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(userList == null || userList.Count() == 0? ResultType.NothingFound : ResultType.OkForQuery);
            if (Response.StatusCode == CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForQuery))
            {
                return userList;
            }
            else
            {
                return Enumerable.Empty<Domain.ApplicationUser>();
            }
        }

        //
        // GET: Account/testuser
        [HttpGet("{username}")]
        public async Task<Domain.ApplicationUser> GetUser(string username)
        {
            var user = await userManager.FindByNameAsync(username);
            if (user == null)
            {
                Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.NothingFound);
                return null;
            }
            else
            {
                Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.OkForQuery);
                return user;
            }
        }

        //TODO: Add a POST for create user

        //TODO: Add a PUT for update user

        //TODO: Add a DELETE for delete user



        private async Task<string> ReadBody()
        {
            using (var reader = new StreamReader(Request.Body))
            {
                return await reader.ReadToEndAsync();
            }
        }
    }
}
