// <copyright file="WebApiConfig.cs" company="Ellie Mae">
// Copyright (c) Ellie Mae. All rights reserved.
// </copyright>

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1600 // Elements must be documented

using EPPS.LightInject;
using EPPS.LightInject.ServiceLocation;
using EPPS.LightInject.WebApi;
using Microsoft.Practices.ServiceLocation;
using Elli.EPPS.DependencyInjection;
using EPPS.Common;
using Elli.EPPS.Integration.Settings;
//[assembly: System.Web.PreApplicationStartMethod(typeof(EPPS.LightInject.Web.LightInjectHttpModuleInitializer), "Initialize")]
namespace Elli.EPPS.Service
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http.Formatting;
    using System.Web.Http;
    using System.Web.Http.Cors;
    
    using Elli.EPPS.Service.Filters;
    using Models.Security;
    
    using Metrics;
    using Models;
    using System.Web.Http.Validation.Providers;
    using Newtonsoft.Json.Converters;
    using Newtonsoft.Json.Serialization;
    using Routing;
    
   
    using Handlers;
    
    using Erg.WebApi;
    using Erg.Correlation;
    using Erg;
    using Erg.Rest;
    using Erg.WebApi.Rest;
    
    
    using Validation;
    using Elli.EPPS.Integration.Common;
    using Elli.EPPS.Integration;
    using Logging;
    using System.Web.Http.Filters;

    
    public static class WebApiConfig
    {
        public const string ServiceContractTitle = "EPPS Service REST API";
        public const int ServiceContractMinVersion = 1;
        public const int ServiceContractMaxVersion = 1;
        public const string VersionArgumentName = "version";

        /// <summary>
        /// Setup for the WebApi application.
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        public static void Register(HttpConfiguration config)
        {
            Register(config, null);
        }

        /// <summary>
        /// Setup for the WebApi application.
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        /// <param name="dependencyInjectionOverrides">ONLY FOR TESTING -
        /// REALLY DO NOT USE THIS EXCEPT FOR TESTING IT WILL STOP PROPER CONTAINER VERIFICATION
        /// CANNOT USE RegisterConditional WHILE USING THIS SOLUTION
        /// </param>
        internal static void Register(HttpConfiguration config, List<Action<ServiceContainer, HttpConfiguration>> dependencyInjectionOverrides)
        {
            var corsAttr = new EnableCorsAttribute("*", "*", "*");
            config.EnableCors(corsAttr);

            var modelValidatorProvider = config.Services.GetModelValidatorProviders()
                .Where(item => item is DataAnnotationsModelValidatorProvider)
                .FirstOrDefault() as DataAnnotationsModelValidatorProvider;
            if (modelValidatorProvider == null)
            {
                throw new InvalidOperationException("Requires DataAnnotationsModelValidatorProvider to function correctly.");
            }

            modelValidatorProvider.RegisterDefaultAdapterFactory(new DataAnnotationsModelValidationFactory((validatorProviders, attribute) =>
            {
                return new WebApiModelValidator(validatorProviders, attribute, config);
            }));

            ConfigureRouting(config);

            ConfigureFormatters(config);

            ConfigureDependencyInjection(config, dependencyInjectionOverrides);

            //var test = config.DependencyResolver.GetService<ICorrelationFilter>();

            //var test1 = config.DependencyResolver.GetService<IRestStorage>();


            //config.RegisterErgFilter(
            //    config.DependencyResolver.GetService<ICorrelationFilter>(),
            //    config.DependencyResolver.GetService<IRestStorage>());

            ConfigureHandlers(config);

            //ConfigureFilters(config);
        }


        /// <summary>
        /// 
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        public static void ConfigureRouting(HttpConfiguration config)
        {
            string virtualPath = config.VirtualPathRoot;

            string globalRoutePrefix = "epps";
            if (virtualPath.EndsWith("/" + globalRoutePrefix, System.StringComparison.OrdinalIgnoreCase) || virtualPath.EndsWith("/" + globalRoutePrefix + "/"))
            {
                globalRoutePrefix = string.Empty;
            }

            var routeProvider = new VersionedRouteProvider(
                globalRoutePrefix: globalRoutePrefix,
                minSupportedVersion: ServiceContractMinVersion,
                maxSupportedVersion: ServiceContractMaxVersion,
                versionArgumentName: VersionArgumentName);

            config.MapHttpAttributeRoutes(routeProvider);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        public static void ConfigureFormatters(HttpConfiguration config)
        {
            config.Formatters.Clear();
            var jsonFormatter = new JsonMediaTypeFormatter();
            var settings = jsonFormatter.SerializerSettings;
            settings.NullValueHandling = Newtonsoft.Json.NullValueHandling.Ignore;
            settings.Formatting = Newtonsoft.Json.Formatting.Indented;
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
            settings.DateFormatHandling = Newtonsoft.Json.DateFormatHandling.IsoDateFormat;
            settings.DateTimeZoneHandling = Newtonsoft.Json.DateTimeZoneHandling.Utc;
            settings.Converters.Add(new StringEnumConverter());
            //settings.Converters.Add(new CustomDataJsonConverter());
            config.Formatters.Add(jsonFormatter);
        }

        /// <summary>
        /// Configures the dependency injection container and backing dependencies used by the application.
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        /// <param name="overrides">ONLY FOR TESTING -
        /// REALLY DO NOT USE THIS EXCEPT FOR TESTING IT WILL STOP PROPER CONTAINER VERIFICATION
        /// CANNOT USE RegisterConditional WHILE USING THIS SOLUTION
        /// </param>
        internal static void ConfigureDependencyInjection(HttpConfiguration config, List<Action<ServiceContainer, HttpConfiguration>> overrides = null)
        {
            var serviceContainer = new ServiceContainer();

            serviceContainer.RegisterApiControllers();

            serviceContainer.Register<Models.IRequestMessageProvider, RequestMessageProvider>(new PerContainerLifetime());
            serviceContainer.Register<Elli.Erg.WebApi.IRequestMessageProvider, RequestMessageProvider>(new PerContainerLifetime());

            serviceContainer.Register<ICaller, RequestCaller>(new PerContainerLifetime());
            serviceContainer.Register<ICorrelationProvider, RequestCaller>(new PerContainerLifetime());

            serviceContainer.Register<IRestStorage, HttpRequestMessageRestStorage>(new PerContainerLifetime());
            serviceContainer.Register<IProvider<IRestStorage>>(factory => new LightInjectProvider<IRestStorage>(serviceContainer), new PerContainerLifetime());
            serviceContainer.Register<ICorrelationSettings, Elli.Erg.Correlation.CorrelationSettings>(new PerContainerLifetime());
            serviceContainer.Register<ICorrelationFilter, CorrelationFilter>(new PerContainerLifetime());
            serviceContainer.Register<IProvider<ICorrelationInfo>, CorrelationProvider>(new PerContainerLifetime());
            //serviceContainer.Register<IProvider<ICorrelationInfo>>(factory => new LightInjectProvider<ICorrelationInfo>(serviceContainer), new PerContainerLifetime());

            serviceContainer.Register<IDurationProvider, ApiTimerDurationProvider>(new PerContainerLifetime());
            serviceContainer.Register<IExecutionTimeProvider, ApiTimerDurationProvider>(new PerContainerLifetime());

            serviceContainer.Register<ICustomerInstance, EllieMaeAuthorization>(new PerContainerLifetime());
            serviceContainer.Register<IApiUser, EllieMaeAuthorization>(new PerContainerLifetime());
            serviceContainer.Register<ICustomerClientId, EllieMaeAuthorization>(new PerContainerLifetime());

            //Register filters
            //serviceContainer.Register<IAuthenticationFilter,EPPSAuthenticationFilter>(GetLifetime());
            //serviceContainer.Register<IExceptionFilter, SignalFxFilter>(GetLifetime());
            //serviceContainer.Register<ActionFilterAttribute, SignalFxFilter>(GetLifetime());
            //serviceContainer.Register<ActionFilterAttribute, UserIdReplacementFilter>(GetLifetime());
            //serviceContainer.Register<IExceptionFilter, ServiceLoggingFilter>(GetLifetime());
            //serviceContainer.Register<ActionFilterAttribute, ServiceLoggingFilter>(GetLifetime());

            //config.Filters.Add(new EPPSAuthenticationFilter(
            //    config.DependencyResolver.GetService<ILogger>(),
            //    config.DependencyResolver.GetService<IAuthenticationSecrets>()));

            //config.Filters.Add(new SignalFxFilter(
            //(IMetricsFactory)config.DependencyResolver.GetService(typeof(IMetricsFactory))));

            //config.Filters.Add(new UserIdReplacementFilter());

            //config.Filters.Add(new ServiceLoggingFilter(
            //    (ILogger)config.DependencyResolver.GetService(typeof(ILogger))));


            LightInjectConfig.Build(serviceContainer);
            InterceptorsManager.Register(serviceContainer);

            config.RegisterErgFilter(
                serviceContainer.GetInstance<ICorrelationFilter>(),
                serviceContainer.GetInstance<IRestStorage>());

            ConfigureFilters(GlobalConfiguration.Configuration,serviceContainer);

            serviceContainer.EnablePerWebRequestScope();
            serviceContainer.EnableWebApi(GlobalConfiguration.Configuration);


            //ServicesManager.Register(serviceContainer);


            // these registrations / settings are specific to the webapi integration for the application
            //serviceContainer.EnableHttpRequestMessageTracking(config);
            //container.RegisterWebApiControllers(config);

            // these registrations are dependent upon the webapi runtime


            //var requestMessageProvider = Lifestyle.Singleton.CreateRegistration<RequestMessageProvider>(container);
            //container.AddRegistration(typeof(Models.IRequestMessageProvider), requestMessageProvider);
            //container.AddRegistration(typeof(Elli.Erg.WebApi.IRequestMessageProvider), requestMessageProvider);




            //var requestCallerRegistration = Lifestyle.Singleton.CreateRegistration<RequestCaller>(container);
            //container.AddRegistration(typeof(ICaller), requestCallerRegistration);
            //container.AddRegistration(typeof(ICorrelationProvider), requestCallerRegistration);

            // shared library setup - ERG


            //container.RegisterSingleton<IRestStorage, HttpRequestMessageRestStorage>();
            //container.RegisterSingleton<IProvider<IRestStorage>, LightInjectProvider<IRestStorage>>();
            //container.RegisterSingleton<Elli.Erg.Correlation.ICorrelationSettings, Elli.Erg.Correlation.CorrelationSettings>();
            //container.RegisterSingleton<ICorrelationFilter, CorrelationFilter>();
            //container.RegisterSingleton<IProvider<ICorrelationInfo>, CorrelationProvider>();

            //var timingProvider = Lifestyle.Singleton.CreateRegistration<ApiTimerDurationProvider>(container);
            //container.AddRegistration(typeof(IDurationProvider), timingProvider);
            //container.AddRegistration(typeof(IExecutionTimeProvider), timingProvider);

            //var ellieMaeAuthorizationRegistration = Lifestyle.Singleton.CreateRegistration<EllieMaeAuthorization>(container);
            //container.AddRegistration(typeof(ICustomerInstance), ellieMaeAuthorizationRegistration);
            //container.AddRegistration(typeof(IApiUser), ellieMaeAuthorizationRegistration);
            //container.AddRegistration(typeof(ICustomerClientId), ellieMaeAuthorizationRegistration);


            //serviceContainer.Register(factory => ServiceLocator.Current
            //        .GetInstance<AwsClientInstantiater<IAmazonKeyManagementService, AmazonKeyManagementServiceClient>>()
            //        .GetAwsClient(credentials => new AmazonKeyManagementServiceClient(credentials)));

            //using (serviceContainer.BeginScope())
            //{
            //    config.RegisterErgFilter(
            //   serviceContainer.GetInstance<ICorrelationFilter>(),
            //   serviceContainer.GetInstance<IRestStorage>());


            //    //var firstInstance = container.GetInstance<IFoo>();
            //    //var secondInstance = container.GetInstance<IFoo>();
            //    //Assert.AreSame(firstInstance, secondInstance);
            //}

            ServiceLocator.SetLocatorProvider(() => new LightInjectServiceLocator(serviceContainer));
            //config.RegisterErgFilter(
            //    config.DependencyResolver.GetService<ICorrelationFilter>(),
            //    config.DependencyResolver.GetService<IRestStorage>());

            //config.RegisterErgFilter(
            //   ServiceLocator.Current.GetInstance<ICorrelationFilter>(),
            //   ServiceLocator.Current.GetInstance<IRestStorage>());



        }

        private static ILifetime GetLifetime(bool isThreadEnabled = false)
        {
            return isThreadEnabled ? (ILifetime)new PerThreadLifetime() : new PerScopeLifetime();
        }

        /// <summary>
        /// Configures global message handlers.
        /// Message handlers occur first in the pipeline but have least access to processed data.
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        public static void ConfigureHandlers(HttpConfiguration config)
        {
            
            config.MessageHandlers.Add(new ApiTimerHandler());
            config.MessageHandlers.Add(new HttpRequestMessageHandler());
        }

        /// <summary>
        /// Configures global filters and filter providers and settings for them to work properly.
        /// Filter come in multiple flavors and execute as part of the pipeline at different points.
        /// Different filter types will have access to different processed data.
        /// </summary>
        /// <param name="config">
        /// The configuration object for WebApi.
        /// Configurations are built on to or extended from this object for application functionality.
        /// </param>
        internal static void ConfigureFilters(HttpConfiguration config, ServiceContainer container)
        {
            config.SuppressHostPrincipal();

            using (container.BeginScope())
            {
                config.Filters.Add(new EPPSAuthenticationFilter(
                container.GetInstance<ILogger>(),
                container.GetInstance<IApplicationSettings>()));

                config.Filters.Add(new SignalFxFilter(
                container.GetInstance<IMetricsFactory>()));

                config.Filters.Add(new UserIdReplacementFilter());

                config.Filters.Add(new ServiceLoggingFilter(
                    container.GetInstance<ILogger>()));

                config.Filters.Add(new ExceptionHandlingFilter());

            }

            //config.Filters.Add(new EPPSAuthenticationFilter(
            //config.DependencyResolver.GetService<ILogger>(),
            //config.DependencyResolver.GetService<IAuthenticationSecrets>()));

            //config.Filters.Add(new SignalFxFilter(
            //(IMetricsFactory)config.DependencyResolver.GetService(typeof(IMetricsFactory))));

            //config.Filters.Add(new UserIdReplacementFilter());

            //config.Filters.Add(new ServiceLoggingFilter(
            //    (ILogger)config.DependencyResolver.GetService(typeof(ILogger))));


            

        }
    }
}
