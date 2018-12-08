/*********************************************************************************
    The MIT License (MIT)

    Copyright (c) 2018 bernhard.richter@gmail.com

    Permission is hereby granted, free of charge, to any person obtaining a copy
    of this software and associated documentation files (the "Software"), to deal
    in the Software without restriction, including without limitation the rights
    to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
    copies of the Software, and to permit persons to whom the Software is
    furnished to do so, subject to the following conditions:

    The above copyright notice and this permission notice shall be included in all
    copies or substantial portions of the Software.

    THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
    IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
    FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
    AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
    LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
    OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
    SOFTWARE.
******************************************************************************
    LightInject.Microsoft.DependencyInjection version 2.1.0
    http://www.lightinject.net/
    http://twitter.com/bernhardrichter
******************************************************************************/

[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1126:PrefixCallsCorrectly", Justification = "Reviewed")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.ReadabilityRules", "SA1101:PrefixLocalCallsWithThis", Justification = "No inheritance")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1402:FileMayOnlyContainASingleClass", Justification = "Single source file deployment.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1633:FileMustHaveHeader", Justification = "Custom header.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1600:ElementsMustBeDocumented", Justification = "All public members are documented.")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("StyleCop.CSharp.MaintainabilityRules", "SA1401:FieldsMustBePrivate", Justification = "Performance")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("MaintainabilityRules", "SA1403", Justification = "One source file")]
[module: System.Diagnostics.CodeAnalysis.SuppressMessage("DocumentationRules", "SA1649", Justification = "One source file")]

namespace LightInject.Microsoft.DependencyInjection
{
    using System;
    using System.Globalization;
    using System.Linq;
    using System.Reflection;
    using global::Microsoft.Extensions.DependencyInjection;

    /// <summary>
    /// Extends the <see cref="IServiceContainer"/> interface.
    /// </summary>
    public static class DependencyInjectionContainerExtensions
    {
        /// <summary>
        /// Creates an <see cref="IServiceProvider"/> based on the given <paramref name="serviceCollection"/>.
        /// </summary>
        /// <param name="container">The target <see cref="IServiceContainer"/>.</param>
        /// <param name="serviceCollection">The <see cref="IServiceCollection"/> that contains information about the services to be registered.</param>
        /// <returns>A configured <see cref="IServiceProvider"/>.</returns>
        public static IServiceProvider CreateServiceProvider(this IServiceContainer container, IServiceCollection serviceCollection)
        {
            var rootScope = container.BeginScope();
            rootScope.Completed += (a, s) => container.Dispose();
            container.Register<IServiceProvider>(factory => new LightInjectServiceProvider(container), new PerRootScopeLifetime(rootScope));
            container.Register<IServiceScopeFactory>(factory => new LightInjectServiceScopeFactory(container), new PerRootScopeLifetime(rootScope));
            RegisterServices(container, rootScope, serviceCollection);
            return new LightInjectServiceScope(rootScope).ServiceProvider;
        }

        private static void RegisterServices(IServiceContainer container, Scope rootScope, IServiceCollection serviceCollection)
        {
            var registrations = serviceCollection.Select(d => CreateServiceRegistration(d, rootScope)).ToList();
            var test = registrations.Where(sr => sr.ServiceType.FullName.StartsWith("Microsoft.Extensions.Options.IOptions")).ToArray();
            for (int i = 0; i < registrations.Count; i++)
            {
                ServiceRegistration registration = registrations[i];
                registration.ServiceName = i.ToString("D8", CultureInfo.InvariantCulture.NumberFormat);
                container.Register(registration);
            }
        }

        private static ServiceRegistration CreateServiceRegistration(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            if (serviceDescriptor.ImplementationFactory != null)
            {
                return CreateServiceRegistrationForFactoryDelegate(serviceDescriptor, rootScope);
            }

            if (serviceDescriptor.ImplementationInstance != null)
            {
                return CreateServiceRegistrationForInstance(serviceDescriptor, rootScope);
            }

            return CreateServiceRegistrationServiceType(serviceDescriptor, rootScope);
        }

        private static ServiceRegistration CreateServiceRegistrationServiceType(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
            registration.ImplementingType = serviceDescriptor.ImplementationType;
            return registration;
        }

        private static ServiceRegistration CreateServiceRegistrationForInstance(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
            registration.Value = serviceDescriptor.ImplementationInstance;
            return registration;
        }

        private static ServiceRegistration CreateServiceRegistrationForFactoryDelegate(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            ServiceRegistration registration = CreateBasicServiceRegistration(serviceDescriptor, rootScope);
            registration.FactoryExpression = CreateFactoryDelegate(serviceDescriptor);
            return registration;
        }

        private static ServiceRegistration CreateBasicServiceRegistration(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            ServiceRegistration registration = new ServiceRegistration
            {
                Lifetime = ResolveLifetime(serviceDescriptor, rootScope),
                ServiceType = serviceDescriptor.ServiceType,
                ServiceName = Guid.NewGuid().ToString(),
            };
            return registration;
        }

        private static ILifetime ResolveLifetime(ServiceDescriptor serviceDescriptor, Scope rootScope)
        {
            if (serviceDescriptor.ImplementationInstance != null)
            {
                return null;
            }

            ILifetime lifetime = null;

            switch (serviceDescriptor.Lifetime)
            {
                case ServiceLifetime.Scoped:
                    lifetime = new PerScopeLifetime();
                    break;
                case ServiceLifetime.Singleton:
                    lifetime = new PerRootScopeLifetime(rootScope);
                    break;
                case ServiceLifetime.Transient:
                    lifetime = new PerRequestLifeTime();
                    break;
            }

            return lifetime;
        }

        private static Delegate CreateFactoryDelegate(ServiceDescriptor serviceDescriptor)
        {
            var openGenericMethod = typeof(DependencyInjectionContainerExtensions).GetTypeInfo().GetDeclaredMethod("CreateTypedFactoryDelegate");
            var closedGenericMethod = openGenericMethod.MakeGenericMethod(serviceDescriptor.ServiceType);
            return (Delegate)closedGenericMethod.Invoke(null, new object[] { serviceDescriptor });
        }

        private static Func<IServiceFactory, T> CreateTypedFactoryDelegate<T>(ServiceDescriptor serviceDescriptor)
        {
            return serviceFactory => (T)serviceDescriptor.ImplementationFactory(serviceFactory.GetInstance<IServiceProvider>());
        }
    }

    /// <summary>
    /// Extends the <see cref="ContainerOptions"/> class.
    /// </summary>
    public static class ContainerOptionsExtensions
    {
        /// <summary>
        /// Sets up the <see cref="ContainerOptions"/> to be compliant with the conventions used in Microsoft.Extensions.DependencyInjection.
        /// </summary>
        /// <param name="options">The target <see cref="ContainerOptions"/>.</param>
        /// <returns><see cref="ContainerOptions"/></returns>
        public static ContainerOptions WithMicrosoftSettings(this ContainerOptions options)
        {
            options.DefaultServiceSelector = serviceNames => serviceNames.SingleOrDefault(string.IsNullOrWhiteSpace) ?? serviceNames.Last();
            options.EnablePropertyInjection = false;
            options.EnableVariance = false;
            return options;
        }
    }


    /// <summary>
    /// Creates a LightInject container builder.
    /// </summary>
    public class LightInjectServiceProviderFactory : IServiceProviderFactory<IServiceContainer>
    {
        private readonly ContainerOptions options;
        private IServiceCollection services;
        private readonly Func<IServiceContainer> containerFactory;

        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
        /// </summary>
        public LightInjectServiceProviderFactory()
            : this(ContainerOptions.Default)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
        /// </summary>
        /// <param name="options">The <see cref="ContainerOptions"/> to be used when creating the <see cref="ServiceContainer"/>.</param>
        public LightInjectServiceProviderFactory(ContainerOptions options)
        {
            this.containerFactory = () => new ServiceContainer(options.WithMicrosoftSettings());
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceProviderFactory"/> class.
        /// </summary>
        /// <param name="serviceContainer">The <see cref="IServiceContainer"/> to be used.</param>
        public LightInjectServiceProviderFactory(IServiceContainer serviceContainer)
        {
            this.containerFactory = () => serviceContainer;
        }


        /// <inheritdoc/>
        public IServiceContainer CreateBuilder(IServiceCollection services)
        {
            this.services = services;
            return containerFactory();
        }

        /// <inheritdoc/>
        public IServiceProvider CreateServiceProvider(IServiceContainer containerBuilder)
        {
            var provider = containerBuilder.CreateServiceProvider(services);
            return provider;
        }
    }

    /// <summary>
    /// An <see cref="IServiceProvider"/> that uses LightInject as the underlying container.
    /// </summary>
    internal class LightInjectServiceProvider : IServiceProvider, IDisposable
    {
        private readonly IServiceFactory serviceFactory;

        private bool isDisposed = false;

        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceProvider"/> class.
        /// </summary>
        /// <param name="serviceFactory">The underlying <see cref="IServiceFactory"/>.</param>
        public LightInjectServiceProvider(IServiceFactory serviceFactory)
        {
            this.serviceFactory = serviceFactory;
        }

        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;

            if (serviceFactory is Scope scope)
            {
                scope.Dispose();
            }
        }

        /// <summary>
        /// Gets an instance of the given <paramref name="serviceType"/>.
        /// </summary>
        /// <param name="serviceType">The service type to return.</param>
        /// <returns>An instance of the given <paramref name="serviceType"/>.</returns>
        public object GetService(Type serviceType)
        {
            Console.WriteLine(serviceType);
            return serviceFactory.TryGetInstance(serviceType);
        }
    }

    /// <summary>
    /// An <see cref="IServiceScopeFactory"/> that uses an <see cref="IServiceContainer"/> to create new scopes.
    /// </summary>
    internal class LightInjectServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IServiceContainer container;

        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceScopeFactory"/> class.
        /// </summary>
        /// <param name="container">The <see cref="IServiceContainer"/> used to create new scopes.</param>
        public LightInjectServiceScopeFactory(IServiceContainer container)
        {
            this.container = container;
        }

        /// <inheritdoc/>
        public IServiceScope CreateScope()
        {
            var scope = container.BeginScope();

            return new LightInjectServiceScope(scope);
        }
    }

    /// <summary>
    /// An <see cref="IServiceScope"/> implementation that wraps a <see cref="Scope"/>.
    /// </summary>
    internal class LightInjectServiceScope : IServiceScope
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LightInjectServiceScope"/> class.
        /// </summary>
        /// <param name="scope">The <see cref="Scope"/> wrapped by this class.</param>
        public LightInjectServiceScope(Scope scope)
        {
            Scope = scope;
            ServiceProvider = new LightInjectServiceProvider(scope);
        }

        /// <inheritdoc/>
        public IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Gets the wrapped <see cref="Scope"/>.
        /// </summary>
        public Scope Scope { get; }

        /// <summary>
        /// Disposes the wrapped <see cref="Scope"/>.
        /// </summary>
        public void Dispose()
        {
            Scope.Dispose();
        }
    }

    /// <summary>
    /// An <see cref="ILifetime"/> implementation that makes it possible to mimic the notion of a root scope.
    /// </summary>
    internal class PerRootScopeLifetime : ILifetime, ICloneableLifeTime
    {
        private readonly ThreadSafeDictionary<Scope, object> instances = new ThreadSafeDictionary<Scope, object>();

        private readonly Scope rootScope;

        /// <summary>
        /// Initializes a new instance of the <see cref="PerRootScopeLifetime"/> class.
        /// </summary>
        /// <param name="rootScope">The root <see cref="Scope"/>.</param>
        public PerRootScopeLifetime(Scope rootScope)
        {
            this.rootScope = rootScope;
        }

        /// <inheritdoc/>
        public object GetInstance(Func<object> createInstance, Scope scope)
        {
            return instances.GetOrAdd(rootScope, s => CreateScopedInstance(createInstance));
        }

        /// <inheritdoc/>
        public ILifetime Clone()
        {
            return new PerRootScopeLifetime(rootScope);
        }

        private void RegisterForDisposal(object instance)
        {
            if (instance is IDisposable disposable)
            {
                rootScope.TrackInstance(disposable);
            }
        }

        private object CreateScopedInstance(Func<object> createInstance)
        {
            rootScope.Completed += OnScopeCompleted;
            var instance = createInstance();

            RegisterForDisposal(instance);
            return instance;
        }

        private void OnScopeCompleted(object sender, EventArgs e)
        {
            var scope = (Scope)sender;
            scope.Completed -= OnScopeCompleted;
            instances.TryRemove(scope, out object removedInstance);
        }
    }
}