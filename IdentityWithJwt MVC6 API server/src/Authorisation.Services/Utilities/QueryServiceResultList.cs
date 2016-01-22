using Microsoft.Data.Entity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace Authorisation.Services.Utilities
{
    /// <summary>
    /// A compound result similar to Service result but is intended for lists of results and ensure a proper list is returned and not a IQueryable resulting in an oData interface
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class QueryServiceResultList<T>
    {
        /// <summary>
        /// This is a factory method rather than a constructor as we want it to be asynchronous and constructors aren't
        /// </summary>
        /// <param name="content"></param>
        /// <param name="resultType"></param>
        /// <param name="useAsync">There are issues using the async calls with fakes so add this (defaulted on) flag to use non-async alternatives in testing</param>
        /// <returns></returns>
        public static async Task<QueryServiceResultList<T>> Create(IQueryable<T> content, ResultType resultType, bool useAsync = true)
        {
            List<T> justAList;
            if (useAsync)
            {
                justAList = await content.ToListAsync();
            }
            else
            {
                justAList = content.ToList();
            }
            return new QueryServiceResultList<T>(justAList, resultType);
        }

        /// <summary>
        /// where for any reason we cannot find the resources we were looking for
        /// </summary>
        /// <param name="resultType"></param>
        /// <returns></returns>
        public static QueryServiceResultList<T> CreateEmpty(ResultType resultType)
        {
            return new QueryServiceResultList<T>(new List<T>(), resultType);
        }

        /// <summary>
        /// Look for a subset
        /// </summary>
        /// <param name="content"></param>
        /// <param name="predicate"></param>
        /// <param name="useAsync">There are issues using the async calls with fakes so add this (defaulted on) flag to use non-async alternatives in testing</param>
        /// <returns></returns>
        public static async Task<QueryServiceResultList<T>> Find(IQueryable<T> content, Expression<Func<T, bool>> predicate, bool useAsync = true)
        {
            List<T> justAList;
            if (useAsync)
            {
                justAList = await content.Where(predicate).ToListAsync();
            }
            else
            {
                justAList = content.Where(predicate).ToList();
            }
            ResultType result;
            if (justAList.Any())
            {
                result = ResultType.OkForQuery;
            }
            else
            {
                result = ResultType.NothingFound;
            }
            return new QueryServiceResultList<T>(justAList, result);
        }


        public QueryServiceResultList(List<T> content, ResultType resultType)
        {
            Content = content;
            ResultType = resultType;
        }

        /// <summary>
        /// Convert the result enum into an HTTP status code
        /// </summary>
        public int HttpResultCode => CmdServiceResult.ResultTypeToHttpCode(ResultType);

        public ResultType ResultType { get; }
        public List<T> Content { get; }
    }
}
