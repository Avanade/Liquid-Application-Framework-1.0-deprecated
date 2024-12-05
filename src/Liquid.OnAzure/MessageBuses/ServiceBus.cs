using Liquid.Base.Interfaces;
using Liquid.Domain;
using Liquid.Domain.Base;
using Liquid.Activation;
using Liquid.Runtime.Configuration.Base;
using Liquid.Runtime.Telemetry;
using Microsoft.Azure.ServiceBus;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Liquid.OnAzure
{
    /// <summary>
    /// Implementation of the communication component between queues and topics of the Azure, this class is specific to azure
    /// </summary>
    public class ServiceBus : LightWorker, IWorkbenchService
    {
        /// <summary>
        /// Implementation of the start process queue and process topic. It must be called  parent before start processes.
        /// </summary>
        public override void Initialize()
        {
            base.Initialize();
            ProcessQueue();
            ProcessSubscription();
        }

        public void ProcessSubscriptione()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Implementation of connection service bus
        /// </summary>
        /// <typeparam name="T">Type of the queue or topic</typeparam>
        /// <param name="item">Item queue or topic</param>
        /// <returns>StringConnection of the ServiceBus</returns>
        private string GetConnection<T>(KeyValuePair<MethodInfo, T> item)
        {
            MethodInfo method = item.Key;
            string connectionKey = GetKeyConnection(method);
            ServiceBusConfiguration config = null;
            if (string.IsNullOrEmpty(connectionKey)) // Load specific settings if provided
            {
                config = LightConfigurator.Config<ServiceBusConfiguration>($"{nameof(ServiceBus)}");
            }
            else
            {
                config = LightConfigurator.Config<ServiceBusConfiguration>($"{nameof(ServiceBus)}_{connectionKey}");
            }

            return config.ConnectionString;
        }


        /// <summary>
        /// If  an error occurs in the processing, this method going to called
        /// </summary>
        /// <param name="exceptionReceivedEventArgs">The Exception Received Event</param>
        /// <returns>The task of processs</returns>
        public Task ExceptionReceivedHandler(ExceptionReceivedEventArgs exceptionReceivedEventArgs)
        {
            //Use the class instead of interface because tracking exceptions directly is not supposed to be done outside AMAW (i.e. by the business code)
            Workbench.Instance.Telemetry.TrackException(exceptionReceivedEventArgs.Exception);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Process the queue defined on the Azure 
        /// </summary>
        /// <returns>The task of Process Queue</returns> 
        public void ProcessQueue()
        {
            try
            {
                foreach (var queue in _queues)
                {
                    MethodInfo method = GetMethod(queue);
                    string queueName = queue.Value.QueueName;
                    ReceiveMode receiveMode = (queue.Value.DeleteAfterRead) ? ReceiveMode.ReceiveAndDelete : ReceiveMode.PeekLock;
                    int takeQuantity = queue.Value.TakeQuantity;

                    //Register Trace on the telemetry 
                    QueueClient queueReceiver = new QueueClient(GetConnection(queue), queueName, receiveMode);

                    //Register the method to process receive message
                    //The RegisterMessageHandler is validate for all register exist on the queue, without need loop for items
                    queueReceiver.RegisterMessageHandler(
                        async (message, token) =>
                        {
                            try
                            {
                                InvokeProcess(method, message.Body);

                                if (queueReceiver.ReceiveMode == ReceiveMode.PeekLock) 
                                {
                                    await queueReceiver.CompleteAsync(message.SystemProperties.LockToken);
                                }
                                    
                            }
                            catch (Exception exRegister)
                            {
                                Exception moreInfo = new Exception($"Exception reading message from queue {queueName}. See inner exception for details. Message={exRegister.Message}", exRegister);
                                //Use the class instead of interface because tracking exceptions directly is not supposed to be done outside AMAW (i.e. by the business code)
                                Workbench.Instance.Telemetry.TrackException(moreInfo);

                                //If there is a error , set DeadLetter on register
                                if (queueReceiver.ReceiveMode == ReceiveMode.PeekLock)
                                {
                                    //This operation is only allowed  PeekLock
                                    await queueReceiver.DeadLetterAsync(message.SystemProperties.LockToken,
                                        $"{exRegister.Message}\n {exRegister.InnerException?.Message}", $"{exRegister.StackTrace}");
                                }
                            }
                        },
                        new MessageHandlerOptions(ExceptionReceivedHandler) { MaxConcurrentCalls = takeQuantity, AutoComplete = false });
                }
            }
            catch (Exception exception)
            {
                Exception moreInfo = new Exception($"Error setting up queue consumption from service bus. See inner exception for details. Message={exception.Message}", exception);
                //Use the class instead of interface because tracking exceptions directly is not supposed to be done outside AMAW (i.e. by the business code)
                Workbench.Instance.Telemetry.TrackException(moreInfo);
            }
        }

        /// <summary>
        /// Method created to connect and process the Topic/Subscription in the azure.
        /// </summary>
        /// <returns></returns>
        private void ProcessSubscription()
        {
            try
            {
                foreach (var topic in _topics)
                {
                    MethodInfo method = GetMethod(topic);
                    string topicName = topic.Value.TopicName;
                    string subscriptName = topic.Value.Subscription;
                    ReceiveMode receiveMode = ReceiveMode.PeekLock;
                    if (topic.Value.DeleteAfterRead)
                    {
                        receiveMode = ReceiveMode.ReceiveAndDelete;
                    }
                    int takeQuantity = topic.Value.TakeQuantity;

                    //Register Trace on the telemetry 
                    SubscriptionClient subscriptionClient = new SubscriptionClient(GetConnection(topic), topicName, subscriptName, receiveMode, null);

                    //Register the method to process receive message
                    //The RegisterMessageHandler is validate for all register exist on the queue, without need loop for items
                    subscriptionClient.RegisterMessageHandler(
                        async (message, cancellationToken) =>
                        {
                            try
                            {
                                InvokeProcess(method, message.Body);
                                await subscriptionClient.CompleteAsync(message.SystemProperties.LockToken);
                            }
                            catch (Exception exRegister)
                            {
                                Exception moreInfo = new Exception($"Exception reading message from topic {topicName} and subscriptName {subscriptName}. See inner exception for details. Message={exRegister.Message}", exRegister);
                                //Use the class instead of interface because tracking exceptions directly is not supposed to be done outside AMAW (i.e. by the business code)
                                Workbench.Instance.Telemetry.TrackException(moreInfo);

                                var exceptionDetails = $"{exRegister.Message}";

                                //If there is a business error or a invlida input, set DeadLetter on register
                                if (subscriptionClient.ReceiveMode == ReceiveMode.PeekLock)
                                {
                                    if (exRegister.InnerException != null)
                                    {
                                        exceptionDetails = $"{exceptionDetails} \n {exRegister.InnerException?.Message}";

                                        if (exRegister.InnerException is InvalidInputException)
                                        {
                                            var inputErrors = (exRegister.InnerException as InvalidInputException).InputErrors;

                                            string jsonString = (new { critics = inputErrors }).ToStringCamelCase();
                                            exceptionDetails = $"{exceptionDetails} \n {jsonString}";

                                            //This operation is only allowed  PeekLock
                                            await subscriptionClient.DeadLetterAsync(message.SystemProperties.LockToken,
                                            exceptionDetails, $"{exRegister.StackTrace}");
                                        }

                                        if (exRegister.InnerException is BusinessValidationException)
                                        {
                                            var inputErrors = (exRegister.InnerException as BusinessValidationException).InputErrors;

                                            string jsonString = (new { critics = inputErrors }).ToStringCamelCase();
                                            exceptionDetails = $"{exceptionDetails} \n {jsonString}";

                                            //This operation is only allowed  PeekLock
                                            await subscriptionClient.DeadLetterAsync(message.SystemProperties.LockToken,
                                            exceptionDetails, $"{exRegister.StackTrace}");
                                        }
                                    }
                                }
                            }

                        }, new MessageHandlerOptions((e) => ExceptionReceivedHandler(e)) { AutoComplete = false, MaxConcurrentCalls = takeQuantity });
                }
            }
            catch (Exception exception)
            {
                Exception moreInfo = new Exception($"Error setting up subscription consumption from service bus. See inner exception for details. Message={exception.Message}", exception);
                //Use the class instead of interface because tracking exceptions directly is not supposed to be done outside AMAW (i.e. by the business code)
                Workbench.Instance.Telemetry.TrackException(moreInfo);
            }
        }

        protected override Task ProcessAsync()
        {
            throw new NotImplementedException();
        }
    }
}
