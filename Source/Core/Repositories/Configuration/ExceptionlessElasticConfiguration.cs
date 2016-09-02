﻿using System;
using System.Linq;
using ElasticMacros;
using Elasticsearch.Net.ConnectionPool;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Repositories.Queries;
using Exceptionless.Core.Serialization;
using Foundatio.Caching;
using Foundatio.Jobs;
using Foundatio.Logging;
using Foundatio.Messaging;
using Foundatio.Queues;
using Foundatio.Repositories.Elasticsearch.Configuration;
using Foundatio.Repositories.Elasticsearch.Queries.Builders;
using Nest;

namespace Exceptionless.Core.Repositories.Configuration {
    public class ExceptionlessElasticConfiguration : ElasticConfiguration {
        public ExceptionlessElasticConfiguration(IQueue<WorkItemData> workItemQueue, ICacheClient cacheClient, IMessageBus messageBus, ILoggerFactory loggerFactory) : base(workItemQueue, cacheClient, messageBus, loggerFactory) {
            // register our custom app query builders
            ElasticQueryBuilder.Default.RegisterDefaults();
            ElasticQueryBuilder.Default.Register(new ExceptionlessSystemFilterQueryBuilder());
            ElasticQueryBuilder.Default.Register(new OrganizationIdQueryBuilder());
            ElasticQueryBuilder.Default.Register(new ProjectIdQueryBuilder());
            ElasticQueryBuilder.Default.Register(new StackIdQueryBuilder());

            _logger.Info().Message($"All new indexes will be created with {Settings.Current.ElasticSearchNumberOfShards} Shards and {Settings.Current.ElasticSearchNumberOfReplicas} Replicas");
            AddIndex(Stacks = new StackIndex(this));
            AddIndex(Events = new EventIndex(this));
            AddIndex(Organizations = new OrganizationIndex(this));

            ElasticQueryBuilder.Default.Register(new ElasticMacroSearchQueryBuilder(new ElasticMacroProcessor(c => {
                foreach (var index in Indexes)
                    foreach (var indexType in index.IndexTypes.OfType<IHaveMacros>()) {
                        indexType.ConfigureMacros(c);
                    }
            })));
            
            // TODO: REMOVE THIS
            ConfigureIndexesAsync(beginReindexingOutdated: false).GetAwaiter().GetResult();
        }

        private static ElasticMacrosConfiguration AddAnalyzedField(ElasticMacrosConfiguration c) {
            return c.AddAnalyzedField("test");
        }

        public StackIndex Stacks { get; }
        public EventIndex Events { get; }
        public OrganizationIndex Organizations { get; }

        protected override IConnectionPool CreateConnectionPool() {
            var serverUris = Settings.Current.ElasticSearchConnectionString.Split(',').Select(url => new Uri(url));
            return new StaticConnectionPool(serverUris);
        }

        protected override void ConfigureSettings(ConnectionSettings settings) {
            settings
                .EnableTcpKeepAlive(30 * 1000, 2000)
                .SetDefaultTypeNameInferrer(p => p.Name.ToLowerUnderscoredWords())
                .SetDefaultPropertyNameInferrer(p => p.ToLowerUnderscoredWords())
                .MaximumRetries(5);

            settings.SetJsonSerializerSettingsModifier(s => {
                s.ContractResolver = new EmptyCollectionElasticContractResolver(settings);
                s.AddModelConverters(_logger);
            });
        }
    }
}
