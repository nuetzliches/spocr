using System;

namespace SpocR.Services
{
    public class SpocrService
    {
        public readonly Version Version;

        public SpocrService()
        {
            Version = GetType().Assembly.GetName().Version;       
        }
    }
}