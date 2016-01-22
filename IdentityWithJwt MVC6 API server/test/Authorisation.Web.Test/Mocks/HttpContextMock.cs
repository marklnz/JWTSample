using Microsoft.AspNet.Http;
using Microsoft.AspNet.Http.Authentication;
using Microsoft.AspNet.Http.Features;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace Authorisation.Web.Test.Mocks
{
    /// <summary>
    /// To be implemented as we need them
    /// </summary>
    public class HttpContextMock : HttpContext
    {
        public HttpContextMock()
        {
        }
        public override IServiceProvider ApplicationServices
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override AuthenticationManager Authentication
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override ConnectionInfo Connection
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override IFeatureCollection Features
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override IDictionary<object, object> Items
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override HttpRequest Request
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override CancellationToken RequestAborted
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override IServiceProvider RequestServices
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override HttpResponse Response
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override ISession Session
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override string TraceIdentifier
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override ClaimsPrincipal User
        {
            get
            {
                throw new NotImplementedException();
            }

            set
            {
                throw new NotImplementedException();
            }
        }

        public override WebSocketManager WebSockets
        {
            get
            {
                throw new NotImplementedException();
            }
        }

        public override void Abort()
        {
            throw new NotImplementedException();
        }
    }
}
