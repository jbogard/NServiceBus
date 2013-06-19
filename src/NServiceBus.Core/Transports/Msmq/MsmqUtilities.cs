namespace NServiceBus.Transports.Msmq
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Messaging;
    using System.Net;
    using System.Net.NetworkInformation;
    using System.Xml;
    using System.Xml.Serialization;

    ///<summary>
    /// MSMQ-related utility functions
    ///</summary>
    public class MsmqUtilities
    {
        /// <summary>
        /// Turns a '@' separated value into a full path.
        /// Format is 'queue@machine', or 'queue@ipaddress'
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static string GetFullPath(Address value)
        {
            IPAddress ipAddress;
            if (IPAddress.TryParse(value.Machine, out ipAddress))
                return PREFIX_TCP + MsmqQueueCreator.GetFullPathWithoutPrefix(value);

            return PREFIX + MsmqQueueCreator.GetFullPathWithoutPrefix(value);
        }

        /// <summary>
        /// Gets the name of the return address from the provided value.
        /// If the target includes a machine name, uses the local machine name in the returned value
        /// otherwise uses the local IP address in the returned value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public static string GetReturnAddress(string value, string target)
        {
            return GetReturnAddress(Address.Parse(value), Address.Parse(target));
        }

        /// <summary>
        /// Gets the name of the return address from the provided value.
        /// If the target includes a machine name, uses the local machine name in the returned value
        /// otherwise uses the local IP address in the returned value.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="target"></param>
        /// <returns></returns>
        public static string GetReturnAddress(Address value, Address target)
        {
            var machine = target.Machine;

            IPAddress targetIpAddress;

            //see if the target is an IP address, if so, get our own local ip address
            if (IPAddress.TryParse(machine, out targetIpAddress))
            {
                if (string.IsNullOrEmpty(localIp))
                    localIp = LocalIpAddress(targetIpAddress);

                return PREFIX_TCP + localIp + PRIVATE + value.Queue;
            }

            return PREFIX + MsmqQueueCreator.GetFullPathWithoutPrefix(value);
        }

        static string LocalIpAddress(IPAddress targetIpAddress)
        {
            var networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            var availableAddresses =
                networkInterfaces.Where(
                    ni =>
                    ni.OperationalStatus == OperationalStatus.Up &&
                    ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                    .SelectMany(ni => ni.GetIPProperties().UnicastAddresses).ToList();

            var firstWithMatchingFamily =
                availableAddresses.FirstOrDefault(a => a.Address.AddressFamily == targetIpAddress.AddressFamily);

            if (firstWithMatchingFamily != null)
                return firstWithMatchingFamily.Address.ToString();

            var fallbackToDifferentFamily = availableAddresses.FirstOrDefault();

            if (fallbackToDifferentFamily != null)
                return fallbackToDifferentFamily.Address.ToString();

            return "127.0.0.1";
        }

        static string localIp;

        /// <summary>
        /// Gets an independent address for the queue in the form:
        /// queue@machine.
        /// </summary>
        /// <param name="q"></param>
        /// <returns></returns>
        public static Address GetIndependentAddressForQueue(MessageQueue q)
        {
            if (q == null)
                return null;

            string[] arr = q.FormatName.Split('\\');
            string queueName = arr[arr.Length - 1];

            int directPrefixIndex = arr[0].IndexOf(DIRECTPREFIX);
            if (directPrefixIndex >= 0)
                return new Address(queueName, arr[0].Substring(directPrefixIndex + DIRECTPREFIX.Length));

            int tcpPrefixIndex = arr[0].IndexOf(DIRECTPREFIX_TCP);
            if (tcpPrefixIndex >= 0)
                return new Address(queueName, arr[0].Substring(tcpPrefixIndex + DIRECTPREFIX_TCP.Length));

            try
            {
                // the pessimistic approach failed, try the optimistic approach
                arr = q.QueueName.Split('\\');
                queueName = arr[arr.Length - 1];
                return new Address(queueName, q.MachineName);
            }
            catch
            {
                throw new Exception("Could not translate format name to independent name: " + q.FormatName);
            }
        }

        /// <summary>
        /// Converts an MSMQ message to a TransportMessage.
        /// </summary>
        /// <param name="m"></param>
        /// <returns></returns>
        public static TransportMessage Convert(Message m)
        {
            var headers = DeserializeMessageHeaders(m);

            var result = new TransportMessage(m.Id, headers)
            {
                Recoverable = m.Recoverable,
                TimeToBeReceived = m.TimeToBeReceived,
                ReplyToAddress = GetIndependentAddressForQueue(m.ResponseQueue)
            };

            result.CorrelationId = GetCorrelationId(m, headers);

            if (Enum.IsDefined(typeof(MessageIntentEnum), m.AppSpecific))
                result.MessageIntent = (MessageIntentEnum)m.AppSpecific;

            m.BodyStream.Position = 0;
            result.Body = new byte[m.BodyStream.Length];
            m.BodyStream.Read(result.Body, 0, result.Body.Length);

            return result;
        }

        static string GetCorrelationId(Message message, Dictionary<string, string> headers)
        {
            string correlationId;

            if (headers.TryGetValue(CorrelationIdHeader, out correlationId))
                return correlationId;

            if (message.CorrelationId == "00000000-0000-0000-0000-000000000000\\0")
                return null;

            //msmq required the id's to be in the {guid}\{incrementing number} format so we need to fake a \0 at the end that the sender added to make it compatible
            return message.CorrelationId.Replace("\\0", "");
        }

        static Dictionary<string, string> DeserializeMessageHeaders(Message m)
        {
            var result = new Dictionary<string, string>();

            if (m.Extension.Length == 0)
                return result;

            object o;
            using (var stream = new MemoryStream(m.Extension))
            {
                using (var reader = XmlReader.Create(stream, new XmlReaderSettings { CheckCharacters = false }))
                {
                    o = headerSerializer.Deserialize(reader);
                }
            }

            foreach (var pair in o as List<HeaderInfo>)
            {
                if (pair.Key != null)
                {
                    result.Add(pair.Key, pair.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Converts a TransportMessage to an Msmq message.
        /// Doesn't set the ResponseQueue of the result.
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static Message Convert(TransportMessage message)
        {
            var result = new Message();

            if (message.Body != null)
            {
                result.BodyStream = new MemoryStream(message.Body);
            }

            Guid correlationId;

            if (Guid.TryParse(message.CorrelationId, out correlationId))
            {
                result.CorrelationId = message.CorrelationId + "\\0";//msmq required the id's to be in the {guid}\{incrementing number} format so we need to fake a \0 at the end to make it compatible                
            }
            else
            {
                if (!message.Headers.ContainsKey(CorrelationIdHeader))
                {
                    message.Headers[CorrelationIdHeader] = message.CorrelationId;
                }
            }

            result.Recoverable = message.Recoverable;

            if (message.TimeToBeReceived < MessageQueue.InfiniteTimeout)
            {
                result.TimeToBeReceived = message.TimeToBeReceived;
            }

            using (var stream = new MemoryStream())
            {
                headerSerializer.Serialize(stream, message.Headers.Select(pair => new HeaderInfo { Key = pair.Key, Value = pair.Value }).ToList());
                result.Extension = stream.ToArray();
            }

            result.AppSpecific = (int)message.MessageIntent;

            return result;
        }

        private const string DIRECTPREFIX = "DIRECT=OS:";
        private static readonly string DIRECTPREFIX_TCP = "DIRECT=TCP:";
        private static readonly string CorrelationIdHeader = "NServiceBus.CorrelationId";
        private readonly static string PREFIX_TCP = "FormatName:" + DIRECTPREFIX_TCP;
        private static readonly string PREFIX = "FormatName:" + DIRECTPREFIX;
        private static readonly XmlSerializer headerSerializer = new XmlSerializer(typeof(List<HeaderInfo>));
        internal const string PRIVATE = "\\private$\\";
    }
}