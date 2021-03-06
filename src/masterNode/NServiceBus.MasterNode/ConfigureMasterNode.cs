﻿using System;
using System.Configuration;

namespace NServiceBus
{
    using Config;

    public static class ConfigureMasterNode
    {
        public static Configure AsMasterNode(this Configure config)
        {
            isMasterNode = true;
            return config;
        }

        public static bool IsConfiguredAsMasterNode(this Configure config)
        {
            return isMasterNode;
        }

        public static string GetMasterNode(this Configure config)
        {
            var section = Configure.GetConfigSection<MasterNodeConfig>();
            if (section != null)
                return section.Node;

            return null;
        }

        public static Address GetMasterNodeAddress(this Configure config)
        {
            var masterNode = GetMasterNode(config);
            
            if (string.IsNullOrWhiteSpace(masterNode))
                return Address.Parse(Configure.EndpointName);

            ValidateHostName(masterNode);

            return new Address(Configure.EndpointName, masterNode);
        }
        
        private static void ValidateHostName(string hostName)
        {
            if (Uri.CheckHostName(hostName) == UriHostNameType.Unknown)
                throw new ConfigurationErrorsException(string.Format("The 'Node' entry in MasterNodeConfig section of the configuration file: '{0}' is not a valid DNS name.", hostName));
        }

        static bool isMasterNode;
    }
}
