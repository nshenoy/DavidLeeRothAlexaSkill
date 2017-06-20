using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using AlexaSkill.Data;
using DavidLeeRothAlexaSkill.Configuration;
using DavidLeeRothAlexaSkill.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
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
        public async Task<IActionResult> GiveMeABottleOfAnythingAndAGlazedDonut([FromBody] AlexaRequest request)
        {
            //try
            //{
            //    this.HttpContext.Request.EnableRewind();
            //    var initialBody = this.HttpContext.Request.Body;
            //    await this.VerifyCertificate(this.HttpContext);
            //    this.HttpContext.Request.Body = initialBody;
            //}
            //catch (CertificateException ce)
            //{
            //    this.logger.LogError(ce.Message, ce);
            //    return BadRequest(ce.Message);
            //}

            if (request.Session.Application.ApplicationId != this.alexaSkillConfiguration.ApplicationId)
            {
                this.logger.LogError("Request ApplicationId is incorrect");
                return BadRequest();
            }

            var totalSeconds = (DateTime.UtcNow - request.Request.Timestamp).TotalSeconds;
            if(totalSeconds <= 0 || totalSeconds > 150)
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

        private async Task VerifyCertificate(HttpContext context)
        {
            var headers = context.Request.Headers;

            if (headers.Keys.Contains("X-IAINTGOTNOBODY"))
            {
                this.logger.LogInformation("X-IAINTGOTNOBODY header found. Skipping certificate validation.");
                return;
            }

            if (!headers.Keys.Contains("Signature") || !headers.Keys.Contains("SignatureCertChainUrl"))
            {
                string err = "Request does not contain `Signature` or `SignatureCertChainUrl` headers";
                throw new CertificateException(err);
            }

            var signatureCertChainUrl = headers["SignatureCertChainUrl"].FirstOrDefault()?.Replace("/../", "/");

            if (string.IsNullOrWhiteSpace(signatureCertChainUrl))
            {
                string err = "Request does not contain `SignatureCertChainUrl` header";
                throw new CertificateException(err);
            }

            var certUrl = new Uri(signatureCertChainUrl);
            if (!((certUrl.Port == 443 || certUrl.IsDefaultPort)
                && certUrl.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase)
                && certUrl.Host.Equals("s3.amazonaws.com", StringComparison.OrdinalIgnoreCase)
                && certUrl.AbsolutePath.StartsWith("/echo.api/")))
            {
                string err = $"`SignatureCertChainUrl` is invalid [{signatureCertChainUrl}]";
                throw new CertificateException(err);
            }

            using (var client = new HttpClient())
            {
                this.logger.LogInformation($"Attempting to download cert from {certUrl}...");
                var cert = await client.GetByteArrayAsync(certUrl);
                var x509cert = new X509Certificate2(cert);
                var effectiveDate = DateTime.MinValue;
                var expiryDate = DateTime.MinValue;

                if ((x509cert.NotBefore > DateTime.UtcNow) || (x509cert.NotAfter < DateTime.UtcNow))
                {
                    string err = "Certificate date is invalid";
                    throw new CertificateException(err);
                }

                if (!x509cert.Subject.Contains("CN=echo-api.amazon.com"))
                {
                    string err = $"Certificate subject '{x509cert.Subject}' incorrect. (IssuerName: '{x509cert.IssuerName.Name}')";
                    throw new CertificateException(err);
                }

                var certChain = new X509Chain();
                certChain.Build(x509cert);
                bool isValidCertChain = true;
                foreach (var chainElement in certChain.ChainElements)
                {
                    if (!chainElement.Certificate.Verify())
                    {
                        isValidCertChain = false;
                        break;
                    }
                }

                if (!isValidCertChain)
                {
                    string err = "Certificate chain is not valid";
                    throw new CertificateException(err);
                }

                this.logger.LogInformation("Attempting to validate Signature hash...");

                var signatureHeaderValue = headers["Signature"].First();

                this.logger.LogInformation($"Signature value: {signatureHeaderValue}");

                var signature = Convert.FromBase64String(signatureHeaderValue);
                using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, leaveOpen: true))
                using (var sha1 = new SHA1Managed())
                {
                    var body = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;

                    this.logger.LogInformation($"Body: {body}");
                    var data = sha1.ComputeHash(Encoding.UTF8.GetBytes(body));

                    var rsa = (RSACryptoServiceProvider)x509cert.PublicKey.Key;

                    if (rsa == null)
                    {
                        string err = "Certificate public key is null";
                        throw new CertificateException(err);
                    }

                    this.logger.LogInformation("Verifying hash...");
                    if (!rsa.VerifyHash(data, CryptoConfig.MapNameToOID("SHA1"), signature))
                    {
                        string err = "Asserted hash value from `Signature` header does not match derived hash value from the request body";
                        throw new CertificateException(err);
                    }
                    this.logger.LogInformation("Done validating certificate!");
                }

            }
        }

    }
}
