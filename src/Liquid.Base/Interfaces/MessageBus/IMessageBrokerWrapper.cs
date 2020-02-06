﻿using System.Threading.Tasks;

namespace Liquid.Interfaces
{   /// <summary>
    /// Programming interface that applications can use to control brokers
    /// </summary>
    public interface IMessageBrokerWrapper
    {
        void Initialize(string tagConfigName, string name);
        Task SendToQueue(object message);
        Task SendToTopic(object message);
    }
}
