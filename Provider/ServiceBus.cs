using Azure.Messaging.ServiceBus.Administration;
using System.Text;
using Microsoft.Extensions.Logging;
using System.Web;
using System.Security.Cryptography;
using System.Globalization;
using System;
using Microsoft.Azure.ServiceBus;
using Microsoft.Azure.ServiceBus.Primitives;
using System.Threading.Tasks;


namespace SecretRotationPOC.ServiceBus
{
    public class ServiceBusService 
    {
        private readonly ILogger _logger;
        private readonly ServiceBusAdministrationClient _serviceBusAdministrationClient;

        public ServiceBusService(ILogger logger)
            : this(logger, new ServiceBusAdministrationClient(Environment.GetEnvironmentVariable("RootManageSharedAccessKeyConnectionString")))
        {
        }

        public ServiceBusService(ILogger logger, ServiceBusAdministrationClient serviceBusAdministrationClient)
        {
            _logger = logger;
            _serviceBusAdministrationClient = serviceBusAdministrationClient;
        }

#nullable enable
        public async Task<AuthorizationRule?> GetSAPolicy(string queueName, string saPolicyName)
        {
            //SharedAccessAuthorizationRule 
            if (!await _serviceBusAdministrationClient.QueueExistsAsync(queueName))
            {
                throw new InvalidOperationException($"Failed to retrieve SA policy. Queue: {queueName} doesn't exist.");
            }

            var queue = (await _serviceBusAdministrationClient.GetQueueAsync(queueName)).Value;
            var policy = queue.AuthorizationRules.Find(rule => rule.KeyName == saPolicyName);

            return policy;
        }
        public async Task<string> CreateSASTokenForPolicy(
            string resourceUri,
            string saPolicyName,
            string queueName,
            TimeSpan expireIn)
        {
            var saPolicy = await GetSAPolicy(queueName, saPolicyName) as SharedAccessAuthorizationRule;
            if (saPolicy == null)
            {
                throw new InvalidOperationException($"Failed to create SAS token. SA policy is missing.");
            }

            return CreateSASToken(resourceUri, saPolicyName, saPolicy.PrimaryKey, expireIn);
        }

        public string CreateSASToken(string resourceUri, string saPolicyName, string saPolicyKey, TimeSpan expireIn)
        {
            TimeSpan sinceEpoch = DateTime.UtcNow - new DateTime(1970, 1, 1);
            var expiry = Convert.ToString((int)sinceEpoch.TotalSeconds + expireIn.TotalSeconds);

            string stringToSign = HttpUtility.UrlEncode(resourceUri) + "\n" + expiry;
            HMACSHA256 hmac = new HMACSHA256(Encoding.UTF8.GetBytes(saPolicyKey));
            var signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign)));
            var sasToken = string.Format(CultureInfo.InvariantCulture, "SharedAccessSignature sr={0}&sig={1}&se={2}&skn={3}", HttpUtility.UrlEncode(resourceUri), HttpUtility.UrlEncode(signature), expiry, saPolicyName);
            return sasToken;
        }

        public async Task<string> GetSASTokenConnectionString(string RootSharedAccessPolicyConnectionString, string queueName, string saPolicyName, TimeSpan expireIn)
        {
            ServiceBusConnectionStringBuilder sourceBuilder = new ServiceBusConnectionStringBuilder(RootSharedAccessPolicyConnectionString);
            var endpoint = sourceBuilder.Endpoint;
            var keyName = sourceBuilder.SasKeyName;
            var keyValue = sourceBuilder.SasKey;
            var validityDuration = expireIn;

            TokenScope tokenScope = TokenScope.Entity;
            var saPolicy = await GetSAPolicy(queueName, saPolicyName) as SharedAccessAuthorizationRule;
            if (saPolicy == null)
            {
                throw new InvalidOperationException($"Failed to create SAS token. SA policy is missing.");
            }
            var provider = (SharedAccessSignatureTokenProvider)TokenProvider.CreateSharedAccessSignatureTokenProvider(saPolicy.KeyName, saPolicy.PrimaryKey, validityDuration, tokenScope);

            var token = provider.GetTokenAsync(endpoint + "/" + queueName, validityDuration).GetAwaiter().GetResult();
            if (token == null)
            {
                throw new InvalidOperationException($"Failed to create SAS token. Could not get SAS token.");
            }
            var sasToken = token.TokenValue;
            var serviceBusConnectionStringBuilder = new ServiceBusConnectionStringBuilder(endpoint, queueName, sasToken);
            if (serviceBusConnectionStringBuilder == null)
            {
                throw new InvalidOperationException($"Failed to create SAS token. Unable to create ServiceBusConnectionStringBuilder.");
            }
            return serviceBusConnectionStringBuilder.GetEntityConnectionString();
        }
    }
}