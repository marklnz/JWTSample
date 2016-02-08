﻿using Authorisation.Domain;
using Authorisation.Services;
using Authorisation.Services.Utilities;
using Microsoft.AspNet.Authentication.JwtBearer;
using Microsoft.AspNet.Authorization;
using Microsoft.AspNet.Builder;
using Microsoft.AspNet.Diagnostics;
using Microsoft.AspNet.Hosting;
using Microsoft.AspNet.Identity.EntityFramework;
using Microsoft.AspNet.Mvc;
using Microsoft.AspNet.Mvc.Cors;
using Microsoft.AspNet.Mvc.Formatters;
using Microsoft.Data.Entity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Authorisation.Web
{
    public class Startup
    {
        const string TokenAudience = "IdentityWithJwtClients";
        const string TokenIssuer = "IdentityWithJwtApi";
        private RsaSecurityKey key;
        private TokenAuthOptions tokenOptions;

        public Startup(IHostingEnvironment env)
        {
            // Set up configuration sources.
            var builder = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .AddEnvironmentVariables();
            Configuration = builder.Build();
        }

        public IConfigurationRoot Configuration { get; set; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddEntityFramework()
                .AddSqlServer();

            services.AddMvc();

            ConfigureSecurityServices(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddConsole(Configuration.GetSection("Logging"));
            loggerFactory.AddDebug();

            app.UseIISPlatformHandler();

            app.UseStaticFiles();

            // Configure the security-specific services that we are using.
            // NOTE: This call must be made BEFORE the call to app.UseMvc() or we'll have exceptions thrown on every request
            ConfigureSecurity(app);

            //TODO: Get the routing working properly for logon and logoff - would like to have static routes for logon, logoff, getaccesstoken
            app.UseMvc();
        }

        // Entry point for the application.
        public static void Main(string[] args) => WebApplication.Run<Startup>(args);

        /// <summary>
        /// Configure the services required for token based security.
        /// </summary>
        /// <param name="services">An instance of a class that implements <c>IServiceCollection</c>, to add the security services to.</param>
        private void ConfigureSecurityServices(IServiceCollection services)
        {
            // Add EF (and the DC for Authorisation)
            services.AddEntityFramework()
                .AddSqlServer()
                .AddDbContext<AuthorisationDbContext>(options =>
                    options.UseSqlServer(Configuration.Get<string>("Data:DefaultConnection:ConnectionString")));

            // Set automatic challenge to false to prevent 401 responses being replaced with a 302 redirecting to a non-existant logon page on this server. This is an API, and has no logon UI. We want to return a
            // 401 response to the client and let them handle it from there.
            services.AddIdentity<ApplicationUser, IdentityRole>(options => { options.Cookies.ApplicationCookie.AutomaticChallenge = false; })
                .AddEntityFrameworkStores<AuthorisationDbContext>()
                .AddDefaultTokenProviders();

            // The file referenced here was generated by a call to the RSAKeyUtils.GenerateKeyAndSave method
            RSAParameters keyParams = RSAKeyUtils.GetKeyParameters(Configuration.Get<string>("JwtTokenSettings:RsaKeyFile"));

            // Create the key, and a set of token options to record signing credentials
            // using that key, along with the other parameters we will need in the
            // token controlller.
            key = new RsaSecurityKey(keyParams);
            tokenOptions = new TokenAuthOptions()
            {
                Issuer = TokenIssuer,
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256Signature),
                AccessTokenValidLifetime = Configuration.Get<int>("JwtTokenSettings:AccessTokenTimeout", 5)
            };

            // Save the token options into an instance so they're accessible to the
            // controller.
            //TODO: AddInstance has been renamed to AddSingleton, as of the nightly builds on 27 November, so this will have to change when we go to using RC2 or RTM
            services.AddInstance<TokenAuthOptions>(tokenOptions);
            services.AddOptions();
            services.Configure<Domain.IdentityOptions>(opt => opt.ApiClientKey = Configuration.Get<string>("JwtTokenSettings:ApiClientKey"));

            // Add our authentication services
            services.AddTransient<AuthenticationQueryService>();
            services.AddTransient<AuthenticationCmdService>();

            // Enable the use of an [Authorize("Bearer")] attribute on methods and classes to protect.
            services.AddAuthorization(auth =>
            {
                auth.AddPolicy("Bearer", new AuthorizationPolicyBuilder()
                    .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme‌​)
                    .RequireAuthenticatedUser().Build());
            });

            services.AddCors(options =>
            {
                options.AddPolicy("AllowAnyOrigin",
                    builder => builder.AllowAnyHeader().AllowAnyMethod().AllowAnyOrigin());
            });
            services.Configure<MvcOptions>(options =>
            {
                options.Filters.Add(new CorsAuthorizationFilterFactory("AllowAnyOrigin"));
            });
        }

        private void ConfigureSecurity(IApplicationBuilder app)
        {
            // Create an exceptionhandler middleware that checks unhandled exceptions and if they are
            // SecurityTokenExceptions (or derived types), or they were raised in the Jwt token processing code, then return
            // a 401 response rather than a 500, which we still return for all other unhandled exceptions.
            app.UseExceptionHandler(appBuilder =>
            {
                appBuilder.Use(async (context, next) =>
                {
                    var error = context.Features[typeof(IExceptionHandlerFeature)] as IExceptionHandlerFeature;
                    // This should be much more intelligent - at the moment only expired
                    // security tokens are caught - might be worth checking other possible
                    // exceptions such as an invalid signature.
                    if (error != null && error.Error is SecurityTokenException || error.Error.Source == "System.IdentityModel.Tokens.Jwt")
                    {
                        // For all token exceptions, we return a plain old 401 with no message body.
                        context.Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.Unauthenticated);
                        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    }
                    else if (error != null && error.Error != null)
                    {
                        // For all other exceptions, return a 500, again with no message body
                        context.Response.StatusCode = CmdServiceResult.ResultTypeToHttpCode(ResultType.InternalServerError);
                        context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    }

                    else await next();
                });
            });

            // Ensure we're using our defined Cors policy for all requests
            app.UseCors("AllowAnyOrigin");

            app.UseJwtBearerAuthentication(options =>
            {
                // DO NOT redirect to login page if authn fails
                options.AutomaticChallenge = false;

                // Basic settings - signing key to validate with, plus the issuer issuer.
                options.TokenValidationParameters.IssuerSigningKey = key;
                options.TokenValidationParameters.ValidIssuer = tokenOptions.Issuer;

                //TODO: We could validate the audience here...check that the token is coming from a valid audience. Not sure if this is necessary though.
                //options.TokenValidationParameters.AudienceValidator = new AudienceValidator(...)
                options.TokenValidationParameters.ValidateAudience = false;

                // When receiving a token, check that we've signed it.
                options.TokenValidationParameters.ValidateSignature = true;

                // When receiving a token, check that it is still valid.
                options.TokenValidationParameters.ValidateLifetime = true;

                // This defines the maximum allowable clock skew - i.e. provides a tolerance on the token expiry time
                // when validating the lifetime. As we're creating the tokens locally and validating them on the same
                // machines which should have synchronised time, this can be set to zero. Where external tokens are
                // used, some leeway here could be useful.
                options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(0);
            });

            app.UseIdentity();
            app.EnsureRolesCreated();
            app.EnsureTestUserCreated();
        }
    }
}
