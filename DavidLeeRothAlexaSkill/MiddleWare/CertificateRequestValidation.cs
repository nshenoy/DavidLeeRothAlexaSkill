using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using DavidLeeRothAlexaSkill.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace DavidLeeRothAlexaSkill.MiddleWare
{
    public class CertificateRequestValidation
    {
        private readonly RequestDelegate requestDelegate;
        private readonly ILogger logger;

        public CertificateRequestValidation(RequestDelegate requestDelegate, ILoggerFactory loggerFactory)
        {
            this.requestDelegate = requestDelegate;
            this.logger = loggerFactory.CreateLogger("CertificateRequestValidation");
        }

        public async Task Invoke(HttpContext context)
        {
            var headers = context.Request.Headers;

            try
            {
                await this.VerifyCertificate(headers);
                await this.requestDelegate.Invoke(context);
            }
            catch (CertificateException ce)
            {
                this.logger.LogError(ce.Message, ce);
                context.Response.StatusCode = 403;
                await context.Response.WriteAsync(ce.Message);
            }
            catch (Exception e)
            {
                this.logger.LogError(e.Message, e);
                throw;
            }
        }

        private async Task VerifyCertificate(IHeaderDictionary headers)
        {
            if(headers.Keys.Contains("X-IAINTGOTNOBODY"))
            {
                return;
            }

            if (!headers.Keys.Contains("Signature") || !headers.Keys.Contains("SignatureCertChainUrl"))
            {
                string err = "Request does not contain `Signature` or `SignatureCertChainUrl` headers";
                throw new CertificateException(err);
            }

            var signatureCertChainUrl = headers["SignatureCertChainUrl"].First().Replace("/../", "/");

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
                string err = "`SignatureCertChainUrl` is invalid";
                throw new CertificateException(err);
            }

            using (var client = new HttpClient())
            {
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
            }
        }
    }
}
