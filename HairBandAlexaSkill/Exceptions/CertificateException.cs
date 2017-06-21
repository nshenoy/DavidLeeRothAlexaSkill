using System;

namespace HairBandAlexaSkill.Exceptions
{
    public class CertificateException : Exception
    {
        public CertificateException() : base()
        {
        }

        public CertificateException(string message) : base(message)
        {
        }

        public CertificateException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
