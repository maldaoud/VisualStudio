﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.ComponentModel.Composition.Hosting;
using System.ComponentModel.Composition.Primitives;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using GitHub.Api;
using GitHub.Factories;
using GitHub.Models;
using GitHub.Services;
using GitHub.VisualStudio;
using GitHub.VisualStudio.Views;
using GitHub.VisualStudio.Views.Dialog.Clone;
using Microsoft.VisualStudio.Shell;
using Rothko;
using Task = System.Threading.Tasks.Task;

namespace GitHubCore
{
    public class CompositionServices
    {
        public CompositionContainer CreateCompositionContainer(ExportProvider defaultExportProvider)
        {
            var catalog = new LoggingCatalog(
                GetCatalog(typeof(DialogService).Assembly), // GitHub.App
                GetCatalog(typeof(GraphQLClientFactory).Assembly), // GitHub.Api
                GetCatalog(typeof(RepositoryCloneView).Assembly), // GitHub.VisualStudio.UI
                GetCatalog(typeof(GitHubPackage).Assembly), // GitHub.VisualStudio
                GetCatalog(typeof(VSGitServices).Assembly), // GitHub.TeamFoundation.16
                GetCatalog(typeof(GitService).Assembly), // GitHub.Exports
                GetCatalog(typeof(NotificationDispatcher).Assembly), // GitHub.Exports.Reactive          
                GetCatalog(typeof(IOperatingSystem).Assembly) // Rothko
            );

            var compositionContainer = new CompositionContainer(catalog, defaultExportProvider);

            var gitHubServiceProvider = new MyGitHubServiceProvider(compositionContainer);
            compositionContainer.ComposeExportedValue<IGitHubServiceProvider>(gitHubServiceProvider);

            var usageTracker = new MyUsageTracker();
            compositionContainer.ComposeExportedValue<IUsageTracker>(usageTracker);

            var loginManager = CreateLoginManager(compositionContainer);
            compositionContainer.ComposeExportedValue<ILoginManager>(loginManager);

            // HACK: Stop ViewLocator from attempting to fetch a global service
            var viewViewModelFactory = compositionContainer.GetExportedValue<IViewViewModelFactory>();
            InitializeViewLocator(viewViewModelFactory);

            return compositionContainer;
        }

        static void InitializeViewLocator(IViewViewModelFactory viewViewModelFactory)
        {
            var factoryProviderFiled = typeof(ViewLocator).GetField("factoryProvider", BindingFlags.Static | BindingFlags.NonPublic);
            factoryProviderFiled.SetValue(null, viewViewModelFactory);
        }

        private static LoginManager CreateLoginManager(CompositionContainer compositionContainer)
        {
            var keychain = compositionContainer.GetExportedValue<IKeychain>();
            var lazy2Fa = new Lazy<ITwoFactorChallengeHandler>(() => compositionContainer.GetExportedValue<ITwoFactorChallengeHandler>());
            var oauthListener = compositionContainer.GetExportedValue<IOAuthCallbackListener>();
            var loginManager = new LoginManager(
                    keychain,
                    lazy2Fa,
                    oauthListener,
                    ApiClientConfiguration.ClientId,
                    ApiClientConfiguration.ClientSecret,
                    ApiClientConfiguration.MinimumScopes,
                    ApiClientConfiguration.RequestedScopes,
                    ApiClientConfiguration.AuthorizationNote,
                    ApiClientConfiguration.MachineFingerprint);
            return loginManager;
        }

        static TypeCatalog GetCatalog(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException e)
            {
                Trace.WriteLine(e);
                foreach (var ex in e.LoaderExceptions)
                {
                    Trace.WriteLine(ex);
                }

                types = e.Types.Where(t => t != null).ToArray();
            }

            var catalog = new TypeCatalog(types);
            return catalog;
        }
    }

    public class MyGitHubServiceProvider : IGitHubServiceProvider
    {
        readonly IServiceProvider serviceProvider;

        public MyGitHubServiceProvider(ExportProvider exportProvider)
        {
            ExportProvider = exportProvider;
            serviceProvider = exportProvider.GetExportedValue<SVsServiceProvider>();
        }

        public T TryGetService<T>() where T : class
        {
            try
            {
                return GetService<T>();
            }
            catch
            {
                return default;
            }
        }

        public T GetService<T>() where T : class
        {
            return GetService<T, T>();
        }

        public TRet GetService<T, TRet>()
            where T : class
            where TRet : class
        {
            var value = ExportProvider.GetExportedValueOrDefault<T>();
            if (value != null)
            {
                return value as TRet;
            }

            value = GetService(typeof(T)) as T;
            if (value != null)
            {
                return value as TRet;
            }

            Trace.WriteLine($"Couldn't find service of type {typeof(T)}");
            return null;
        }

        public object GetService(Type serviceType)
        {
            return serviceProvider.GetService(serviceType);
        }

        #region obsolete

        public IServiceProvider GitServiceProvider { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void AddService(Type t, object owner, object instance)
        {
            throw new NotImplementedException();
        }

        public void AddService<T>(object owner, T instance) where T : class
        {
            throw new NotImplementedException();
        }

        public void RemoveService(Type t, object owner)
        {
            throw new NotImplementedException();
        }

        public object TryGetService(Type t)
        {
            throw new NotImplementedException();
        }

        public object TryGetService(string typeName)
        {
            throw new NotImplementedException();
        }

        #endregion

        public ExportProvider ExportProvider { get; }
    }

    public class MyUsageTracker : IUsageTracker
    {
        public Task IncrementCounter(Expression<Func<UsageModel.MeasuresModel, int>> counter)
        {
            Trace.WriteLine($"IncrementCounter {counter}");
            return Task.CompletedTask;
        }
    }

    public class LoggingCatalog : AggregateCatalog
    {
        public LoggingCatalog(params ComposablePartCatalog[] catalogs) : base(catalogs) { }

        public override IEnumerable<Tuple<ComposablePartDefinition, ExportDefinition>> GetExports(ImportDefinition definition)
        {
            var exports = base.GetExports(definition);
            if (exports.Count() == 0)
            {
                Trace.WriteLine($"No exports for {definition}");
            }

            return exports;
        }
    }
}
