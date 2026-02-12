using System;
using System.Collections.Generic;
using System.Linq;
using Confluent.Kafka;

namespace Gateway.Infrastructure.Kafka;

/// <summary>
/// Helper class for Azure Event Hubs configuration
/// </summary>
internal static class EventHubsHelper
{
    /// <summary>
    /// Parses Event Hubs connection string and returns configuration dictionary
    /// </summary>
    public static Dictionary<string, string> ParseEventHubsConnectionString(string connectionString)
    {
        var config = new Dictionary<string, string>();
        
        // Parse connection string
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries);
        var connectionStringDict = new Dictionary<string, string>();
        
        foreach (var part in parts)
        {
            var keyValue = part.Split('=', 2);
            if (keyValue.Length == 2)
            {
                connectionStringDict[keyValue[0].Trim()] = keyValue[1].Trim();
            }
        }
        
        // Extract namespace from Endpoint
        if (connectionStringDict.TryGetValue("Endpoint", out var endpoint))
        {
            // Format: sb://{namespace}.servicebus.windows.net/
            var uri = new Uri(endpoint);
            var namespaceName = uri.Host.Split('.').FirstOrDefault() ?? "";
            var bootstrapServers = $"{namespaceName}.servicebus.windows.net:9093";
            
            config["bootstrap.servers"] = bootstrapServers;
            config["security.protocol"] = "SASL_SSL";
            config["sasl.mechanism"] = "PLAIN";
            config["sasl.username"] = "$ConnectionString";
            config["sasl.password"] = connectionString;
        }
        
        // Extract Event Hub name from EntityPath
        if (connectionStringDict.TryGetValue("EntityPath", out var entityPath))
        {
            config["eventhub.name"] = entityPath;
        }
        
        // Store full connection string for reference
        config["connection.string"] = connectionString;
        
        return config;
    }
    
    /// <summary>
    /// Applies Event Hubs configuration to ProducerConfig
    /// </summary>
    public static void ApplyEventHubsConfig(ProducerConfig config, Dictionary<string, string> eventHubsConfig)
    {
        if (eventHubsConfig.TryGetValue("bootstrap.servers", out var bootstrapServers))
        {
            config.BootstrapServers = bootstrapServers;
        }
        
        if (eventHubsConfig.ContainsKey("security.protocol"))
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
        }
        
        if (eventHubsConfig.ContainsKey("sasl.mechanism"))
        {
            config.SaslMechanism = SaslMechanism.Plain;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.username", out var username))
        {
            config.SaslUsername = username;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.password", out var password))
        {
            config.SaslPassword = password;
        }
    }
    
    /// <summary>
    /// Applies Event Hubs configuration to ConsumerConfig
    /// </summary>
    public static void ApplyEventHubsConfig(ConsumerConfig config, Dictionary<string, string> eventHubsConfig)
    {
        if (eventHubsConfig.TryGetValue("bootstrap.servers", out var bootstrapServers))
        {
            config.BootstrapServers = bootstrapServers;
        }
        
        if (eventHubsConfig.ContainsKey("security.protocol"))
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
        }
        
        if (eventHubsConfig.ContainsKey("sasl.mechanism"))
        {
            config.SaslMechanism = SaslMechanism.Plain;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.username", out var username))
        {
            config.SaslUsername = username;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.password", out var password))
        {
            config.SaslPassword = password;
        }
    }
    
    /// <summary>
    /// Applies Event Hubs configuration to AdminClientConfig
    /// </summary>
    public static void ApplyEventHubsConfig(AdminClientConfig config, Dictionary<string, string> eventHubsConfig)
    {
        if (eventHubsConfig.TryGetValue("bootstrap.servers", out var bootstrapServers))
        {
            config.BootstrapServers = bootstrapServers;
        }
        
        if (eventHubsConfig.ContainsKey("security.protocol"))
        {
            config.SecurityProtocol = SecurityProtocol.SaslSsl;
        }
        
        if (eventHubsConfig.ContainsKey("sasl.mechanism"))
        {
            config.SaslMechanism = SaslMechanism.Plain;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.username", out var username))
        {
            config.SaslUsername = username;
        }
        
        if (eventHubsConfig.TryGetValue("sasl.password", out var password))
        {
            config.SaslPassword = password;
        }
    }
}

