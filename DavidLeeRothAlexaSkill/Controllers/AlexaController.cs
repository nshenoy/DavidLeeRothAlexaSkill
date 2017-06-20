using System;
using AlexaSkill.Data;
using DavidLeeRothAlexaSkill.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DavidLeeRothAlexaSkill.Controllers
{
    public class DavidLeeRothController : Controller
    {
        private static string[] RothResponses = {
            "bopx.mp3",
            "bosdibodiboppx.mp3",
            "r2x.mp3",
            "r3x.mp3",
            "r4x.mp3",
            "whosaidthatx.mp3"
        };

        private static Random GlobalRandom = new Random(Guid.NewGuid().GetHashCode());

        private string randomRothResponse
        {
            get
            {
                var rand = new Random(DavidLeeRothController.GlobalRandom.Next());
                return DavidLeeRothController.RothResponses[rand.Next(0, DavidLeeRothController.RothResponses.Length - 1)];
            }
        }

        private AlexaSkillConfiguration alexaSkillConfiguration;
        private readonly ILogger logger;

        public DavidLeeRothController(IOptions<AlexaSkillConfiguration> alexaSkillConfiguration, ILoggerFactory loggerFactory)
        {
            this.alexaSkillConfiguration = alexaSkillConfiguration.Value;
            this.logger = loggerFactory.CreateLogger("ControllerLogger");
        }

        [Route("api/alexa")]
        [HttpGet]
        public IActionResult Hello()
        {
            return Ok();
        }

        [Route("api/alexa")]
        [HttpPost]
        public IActionResult GiveMeABottleOfAnythingAndAGlazedDonut([FromBody] AlexaRequest request)
        {
            if (request.Session.Application.ApplicationId != this.alexaSkillConfiguration.ApplicationId)
            {
                this.logger.LogError("Request ApplicationId is incorrect");
                return BadRequest();
            }

            var totalSeconds = (int)((DateTime.UtcNow - request.Request.Timestamp).TotalSeconds);
            if(totalSeconds < -5 || totalSeconds > 150)
            {
                this.logger.LogError($"Request timestamp is outside the tolerance bounds ({totalSeconds})");
                return BadRequest($"Request timestamp is outside the tolerance bounds ({totalSeconds})");
            }

            AlexaResponse response = null;

            switch (request.Request.Type)
            {
                case "IntentRequest":
                    response = this.IntentRequestHandler(request);
                    break;
                case "LaunchRequest":
                    response = this.LaunchRequestHandler(request);
                    break;
                case "SessionEndedRequest":
                    response = this.SessionEndedRequestHandler(request);
                    break;
            }

            return Ok(response);
        }

        private AlexaResponse IntentRequestHandler(AlexaRequest request)
        {
            AlexaResponse response = null;

            switch (request.Request.Intent.Name)
            {
                case "SayIntent":
                    response = this.SayIntentHandler(request);
                    break;
                case "AMAZON.HelpIntent":
                    response = this.HelpIntentHandler(request);
                    break;
                case "AMAZON.StopIntent":
                case "AMAZON.CancelIntent":
                    response = new AlexaResponse("");
                    response.Response.ShouldEndSession = true;
                    break;
            }

            return response;
        }

        private AlexaResponse HelpIntentHandler(AlexaRequest request)
        {
            var response = new AlexaResponse("The Hair Band skill is here to melt your face. Try asking it to melt your face, to sing, or to say something. What would you like to do?");
            response.Response.Card.Content = "Awwwww yeah!";
            response.Response.Reprompt.OutputSpeech.Text = "What would you like me to do?";
            response.Response.ShouldEndSession = false;

            return response;
        }

        private AlexaResponse SayIntentHandler(AlexaRequest request)
        {
            var baseUrl = $"https://{this.Request.Host}";
            var ssmlResponse = $"<speak> <audio src=\"{baseUrl}/Sounds/{this.randomRothResponse}\"></audio> </speak>";

            var response = new AlexaResponse();
            response.Response.OutputSpeech.Type = "SSML";
            response.Response.OutputSpeech.Ssml = ssmlResponse;
            response.Response.Card.Content = "Awwwww yeah!";

            return response;
        }

        private AlexaResponse LaunchRequestHandler(AlexaRequest request)
        {
            var response = new AlexaResponse("Welcome to Hair Band. You can tell me to melt your face.");
            response.Response.Card.Content = "Awwwww yeah!";
            response.Response.Reprompt.OutputSpeech.Text = "Please tell me to say something."; 
            response.Response.ShouldEndSession = false; 

            return response;
        }

        private AlexaResponse SessionEndedRequestHandler(AlexaRequest request)
        {
            return null;
        }
    }
}
