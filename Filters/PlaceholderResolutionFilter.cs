using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web.Mvc;
using System.Web.Routing;
using IDeliverable.Donuts.Helpers;
using IDeliverable.Donuts.Services;
using Orchard;
using Orchard.Mvc.Filters;
using Orchard.UI.Admin;

namespace IDeliverable.Donuts.Filters
{
    public class PlaceholderResolutionFilter : FilterProvider, IResultFilter
    {
        public PlaceholderResolutionFilter(IWorkContextAccessor workContextAccessor, RequestContext requestContext)
        {
            mWorkContextAccessor = workContextAccessor;
            _requestContext = requestContext;
        }

        private readonly IWorkContextAccessor mWorkContextAccessor;
        private readonly RequestContext _requestContext;

        public void OnResultExecuted(ResultExecutedContext filterContext)
        {
            // This filter is not reentrant (multiple executions within the same request are
            // not supported) so child actions are ignored completely.
            if (filterContext.IsChildAction)
                return;

            if (AdminFilter.IsApplied(filterContext.RequestContext))
                return;

            var workContext = mWorkContextAccessor.GetContext();

            var currentCulture = workContext.CurrentCulture;
            var currentSite = workContext.CurrentSite;
            var currentUser = workContext.CurrentUser;
            var currentCalendar = workContext.CurrentCalendar;
            var currentTheme = workContext.CurrentTheme;
            var currentTimeZone = workContext.CurrentTimeZone;

            var response = filterContext.HttpContext.Response;
            var captureStream = new PlaceholderStream(response.Filter);
            response.Filter = captureStream;
            captureStream.TransformStream += stream =>
            {
                using (var scope = mWorkContextAccessor.CreateWorkContextScope(_requestContext.HttpContext))
                {
                    scope.WorkContext.CurrentCulture = currentCulture;
                    scope.WorkContext.CurrentSite = currentSite;
                    scope.WorkContext.CurrentUser = currentUser;
                    scope.WorkContext.CurrentCalendar = currentCalendar;
                    scope.WorkContext.CurrentTheme = currentTheme;
                    scope.WorkContext.CurrentTimeZone = currentTimeZone;

                    var html = filterContext.HttpContext.Request.ContentEncoding.GetString(stream.ToArray());

                    html = scope.Resolve<IPlaceholderService>().ResolvePlaceholders(html);
                    // we might have changed the result html we are sending out, so we should be updating
                    // the etag we are sending the browser.
                    var etag = GetHash(html);
                    _requestContext.HttpContext.Response.Headers["ETag"] = etag;
                    var buffer = filterContext.HttpContext.Request.ContentEncoding.GetBytes(html);
                    // check whether the content has changed compared to what the client already claims to have
                    var requestEtag = filterContext.HttpContext.Request.Headers["If-None-Match"];
                    if (!string.IsNullOrWhiteSpace(requestEtag)) {
                        if (string.Equals(requestEtag, etag, StringComparison.Ordinal)) {
                            filterContext.Result = new HttpStatusCodeResult(HttpStatusCode.NotModified);
                        }
                    }

                    return new MemoryStream(buffer);
                };
            };
        }

        public void OnResultExecuting(ResultExecutingContext filterContext)
        {
        }

        private static string GetHash(string input) {

            // Convert the input string to a byte array and compute the hash.
            byte[] data = SHA1.Create().ComputeHash(Encoding.UTF8.GetBytes(input));

            // Create a new Stringbuilder to collect the bytes
            // and create a string.
            var sBuilder = new StringBuilder();

            // Loop through each byte of the hashed data 
            // and format each one as a hexadecimal string.
            for (int i = 0; i < data.Length; i++) {
                sBuilder.Append(data[i].ToString("x2"));
            }

            // Return the hexadecimal string.
            return sBuilder.ToString();
        }
    }
}