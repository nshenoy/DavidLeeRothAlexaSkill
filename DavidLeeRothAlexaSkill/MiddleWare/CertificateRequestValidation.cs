using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using DavidLeeRothAlexaSkill.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using Microsoft.Extensions.Logging;

namespace DavidLeeRothAlexaSkill.MiddleWare
{
    public class CertificateRequestValidation
    {
        private readonly RequestDelegate next;
        private readonly ILogger logger;

        public CertificateRequestValidation(RequestDelegate next, ILoggerFactory loggerFactory)
        {
            this.next = next;
            this.logger = loggerFactory.CreateLogger("CertificateRequestValidation");
        }

        public async Task Invoke(HttpContext context)
        {
            var initialBody = context.Request.Body;

            context.Request.EnableRewind();

            try
            {
                this.logger.LogInformation("Verifying certificate...");
                await this.VerifyCertificate(context);
                this.logger.LogInformation("DONE Verifying certificate.");
                await this.next(context);
            }
            catch (CertificateException ce)
            {
                this.logger.LogError(ce.Message, ce);
                context.Response.StatusCode = 400;
                await context.Response.WriteAsync(ce.Message);
            }
            catch (Exception e)
            {
                this.logger.LogError(e.Message, e);
                context.Response.StatusCode = 400;
                throw;
            }
            finally
            {
                context.Request.Body = initialBody;
            }
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
                using(var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, leaveOpen:true))
                using (var sha1 = new SHA1Managed())
                {
                    var body = await reader.ReadToEndAsync();
                    this.logger.LogInformation($"Body: {body}");
                    var data = sha1.ComputeHash(Encoding.UTF8.GetBytes(body));

                    var rsa = (RSACryptoServiceProvider)x509cert.PublicKey.Key;

                    if(rsa == null)
                    {
                        string err = "Certificate public key is null";
                        throw new CertificateException(err);
                    }

                    this.logger.LogInformation("Verifying hash...");
                    if(!rsa.VerifyHash(data, CryptoConfig.MapNameToOID("SHA1"), signature))
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
