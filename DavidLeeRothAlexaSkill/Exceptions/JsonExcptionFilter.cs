using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace DavidLeeRothAlexaSkill.Exceptions
{
    public class JsonExceptionFilter : IExceptionFilter
    {
        private readonly ILogger logger;

        public JsonExceptionFilter(ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger("JsonExceptionFilter");
        }
        public void OnException(ExceptionContext context)
        {
            var result = new ObjectResult(new
            {
                code = 500,
                message = "A server error occurred.",
                detailedMessage = context.Exception.Message
            });

            result.StatusCode = 500;
            context.Result = result;
            logger.LogError(context.Exception.Message);
            logger.LogError(context.Exception.StackTrace);
        }
    }
}
