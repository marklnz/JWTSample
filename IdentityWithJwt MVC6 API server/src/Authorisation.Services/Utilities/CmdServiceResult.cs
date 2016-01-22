using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Authorisation.Services.Utilities
{
    /// <summary>
    /// A set of possible results from the service layer that can be mapped back to the standard HTTP results
    /// </summary>
    public enum ResultType
    {
        /// <summary>
        /// 200: Used for the successful responses to queries that get data back
        /// </summary>
        OkForQuery,

        /// <summary>
        /// 201: Generally only for successful responses to posts that return just the id of the new item
        /// </summary>
        OkResourceCreated,

        /// <summary>
        /// 202: For responding early to commands that will continue in the background
        /// </summary>
        OkStillProcessing,

        /// <summary>
        /// 204: Used for the succesful responses to commands where there is no content to return
        /// </summary>
        OkForCommand,

        /// <summary>
        /// 400: We don't understand the request - bad parameters or something else
        /// </summary>
        BadRequest,

        /// <summary>
        /// 401: This should be generated earlier in the pipeline before the controller and service
        /// </summary>
        Unauthenticated,

        /// <summary>
        /// 403: We know who they are, but they are not allowed to do this
        /// </summary>
        AccessDenied,

        /// <summary>
        /// 404: Asking for something that does not exist
        /// </summary>
        NothingFound,

        /// <summary>
        /// 409: Asking for something in the wrong state, editing a suspended user for example
        /// </summary>
        StatusConflict,

        /// <summary>
        /// 501: We could use this while developing so the client code will know at least the route is correct
        /// </summary>
        NotImplementedYet,

        /// <summary>
        /// 500: Internal server error, failure in the service for some reason - usually in the exception handler
        /// </summary>
        InternalServerError,

        /// <summary>
        /// 418: This is a real code - it's even documented on Wikipedia
        /// </summary>
        ImATeapot
    }

    /// <summary>
    /// Command services do not return data by definition
    /// EXCEPT When we add a new item, we need to return the identity of the new item
    /// </summary>
    public class CmdServiceResult
    {
        public CmdServiceResult(ResultType resultType)
        {
            NewId = null;
            ResultType = resultType;
        }

        /// <summary>
        /// Called after adding a new item, the only time data is returned on a command
        /// </summary>
        /// <param name="newId"></param>
        public CmdServiceResult(int newId)
        {
            NewId = newId;
            ResultType = ResultType.OkResourceCreated;
        }

        /// <summary>
        /// Convert the result enum into an HTTP status code
        /// </summary>
        public int HttpResponseCode => ResultTypeToHttpCode(this.ResultType);

        public int? NewId { get; }
        public ResultType ResultType { get; }

        public static int ResultTypeToHttpCode(ResultType resultType)
        {
            switch (resultType)
            {
                case ResultType.OkForQuery:
                    return 200;
                case ResultType.OkResourceCreated:
                    return 201;
                case ResultType.OkStillProcessing:
                    return 202;
                case ResultType.OkForCommand:
                    return 204;
                case ResultType.BadRequest:
                    return 400;
                case ResultType.Unauthenticated:
                    return 401;
                case ResultType.AccessDenied:
                    return 403;
                case ResultType.NothingFound:
                    return 404;
                case ResultType.StatusConflict:
                    return 409;
                case ResultType.NotImplementedYet:
                    return 501;
                case ResultType.InternalServerError:
                    return 500;
                case ResultType.ImATeapot:
                default:
                    return 418;
            }
        }
    }
}
