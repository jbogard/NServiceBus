﻿using System;
using System.Collections.Generic;
using System.Linq;
using Raven.Client;

namespace NServiceBus.Unicast.Subscriptions.Raven
{
    public class RavenSubscriptionStorage : ISubscriptionStorage
    {
        public IDocumentStore Store { get; set; }

        public void Init()
        {
        }

        void ISubscriptionStorage.Subscribe(Address client, IEnumerable<MessageType> messageTypes)
        {
            var messageTypeLookup = messageTypes.ToDictionary(Subscription.FormatId);

            using (var session = OpenSession())
            {
                session.Advanced.UseOptimisticConcurrency = true;

                var existingSubscriptions = GetSubscriptions(messageTypeLookup.Values, session).ToLookup(m => m.Id);

                var newAndExistingSubscriptions = messageTypeLookup
                    .Select(id => existingSubscriptions[id.Key].SingleOrDefault() ?? StoreNewSubscription(session, id.Key, id.Value))
                    .Where(subscription => !subscription.Clients.Any(c => c == client)).ToArray();

                foreach (var subscription in newAndExistingSubscriptions)
                {
                    subscription.Clients.Add(client);
                }

                session.SaveChanges();
            }
        }

        void ISubscriptionStorage.Unsubscribe(Address client, IEnumerable<MessageType> messageTypes)
        {
            using (var session = OpenSession())
            {
                session.Advanced.UseOptimisticConcurrency = true;

                var subscriptions = GetSubscriptions(messageTypes, session);

                foreach (var subscription in subscriptions)
                {
                    subscription.Clients.Remove(client);
                }

                session.SaveChanges();
            }
        }

        IEnumerable<Address> ISubscriptionStorage.GetSubscriberAddressesForMessage(IEnumerable<MessageType> messageTypes)
        {
            using (var session = OpenSession())
            {
                var subscriptions = GetSubscriptions(messageTypes, session);
                var clients = subscriptions
                    .SelectMany(s => s.Clients)
                    .Distinct();

                return clients;
            }
        }

        IDocumentSession OpenSession()
        {
            var session = Store.OpenSession();

            session.Advanced.AllowNonAuthoritativeInformation = false;

            return session;
        }

        static IEnumerable<Subscription> GetSubscriptions(IEnumerable<MessageType> messageTypes, IDocumentSession session)
        {
            var ids = messageTypes
                .Select(Subscription.FormatId)
                .ToArray();

            return ids.Select(session.Load<Subscription>).Where(s => s != null);
        }

        static Subscription StoreNewSubscription(IDocumentSession session, string id, MessageType messageType)
        {
            var subscription = new Subscription { Clients = new List<Address>(), Id = id, MessageType = messageType };
            session.Store(subscription);

            return subscription;
        }
    }
}
