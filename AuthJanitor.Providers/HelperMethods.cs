﻿using McMaster.NETCore.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace AuthJanitor.Providers
{
    public static class HelperMethods
    {
        private const string PROVIDER_SEARCH_MASK = "AuthJanitor.Providers.*.dll";
        private const string CHARS_ALPHANUMERIC_ONLY = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        private static Type[] PROVIDER_SHARED_TYPES = new Type[]
        {
            typeof(IAuthJanitorProvider),
            typeof(AuthJanitorProvider<>),
            typeof(IApplicationLifecycleProvider),
            typeof(ApplicationLifecycleProvider<>),
            typeof(IRekeyableObjectProvider),
            typeof(RekeyableObjectProvider<>),
            typeof(IServiceCollection),
            typeof(ILogger)
        };

        public static string GenerateCryptographicallySecureString(int length, string chars = CHARS_ALPHANUMERIC_ONLY)
        {
            // Cryptography Tip!
            // https://cmvandrevala.wordpress.com/2016/09/24/modulo-bias-when-generating-random-numbers/
            // Using modulus to wrap around the source string tends to mathematically favor lower index values for
            //   smaller values of RAND_MAX (here it is LEN(chars)=62). To overcome this bias, we generate the randomness as
            //   4 bytes (int32) per single character we need, to maximize the value of RAND_MAX inside the RNG (as Int32.Max).
            //   Once the value comes out, though, we can introduce modulus again because RAND_MAX is based on the
            //   entropy going into the byte array rather than a fixed set (0,LEN(chars)) -- that makes it sufficiently
            //   large to overcome bias as seen by chi-squared. (Bias approaching zero)
            // * There is some evidence to suggest this has been taken into account in newer versions of NET. *

            byte[] data = new byte[4 * length];
            using (RNGCryptoServiceProvider crypto = new RNGCryptoServiceProvider())
            {
                crypto.GetBytes(data);
            }

            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                int randomNumber = BitConverter.ToInt32(data, i * 4);
                sb.Append(chars[randomNumber % chars.Length]);
            }
            return sb.ToString();
        }

        private static List<Type> _providerTypes = new List<Type>();
        public static IServiceProvider ServiceProvider { get; set; }
        public static void InitializeServiceProvider(ILoggerFactory loggerFactory)
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton(loggerFactory);

            foreach (var libraryFile in Directory.GetFiles(
                AppDomain.CurrentDomain.BaseDirectory,
                PROVIDER_SEARCH_MASK,
                new EnumerationOptions() { RecurseSubdirectories = true }))
            {
                var loader = PluginLoader.CreateFromAssemblyFile(
                    assemblyFile: libraryFile,
                    sharedTypes: PROVIDER_SHARED_TYPES);

                foreach (var providerType in
                    loader.LoadDefaultAssembly()
                          .GetTypes()
                          .Where(t => !t.IsAbstract && t.IsSubclassOf(typeof(IAuthJanitorProvider))))
                {
                    _providerTypes.Add(providerType);
                    serviceCollection.AddTransient(providerType);
                }
            }

            ServiceProvider = serviceCollection.BuildServiceProvider();
        }

        public static async Task RunRekeyingWorkflow(
            TimeSpan requestedValidPeriod,
            params IAuthJanitorProvider[] providers)
        {
            var alcProviders = providers.Where(p => p is IApplicationLifecycleProvider).Cast<IApplicationLifecycleProvider>().ToList();
            var rkoProviders = providers.Where(p => p is IRekeyableObjectProvider).Cast<IRekeyableObjectProvider>().ToList();

            bool testFailed = false;
            await Task.WhenAll(providers.Select(p => p.Test().ContinueWith(task => { if (!task.Result) testFailed = true; })));
            if (testFailed)
                throw new Exception("Sanity check failed!");
            
            var log = ServiceProvider.GetService<ILoggerFactory>().CreateLogger("RekeyingWorkflow");
            log.LogInformation("Preparing {0} Application Lifecycle Providers...", alcProviders.Count);
            try
            {
                await Task.WhenAll(alcProviders.Select(alcProvider => alcProvider.BeforeRekeying()));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error executing BeforeRekeying on Application Lifecycle Provider(s)");
                throw new Exception("Error executing BeforeRekeying on Application Lifecycle Provider(s)");
            }


            log.LogInformation("Rekeying {0} Rekeyable Object Providers...", rkoProviders.Count);
            List<RegeneratedSecret> generatedSecrets = new List<RegeneratedSecret>();
            try
            {
                await Task.WhenAll(rkoProviders.Select(rop => 
                            rop.Rekey(requestedValidPeriod)
                            .ContinueWith(task => generatedSecrets.Add(task.Result))));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error executing Rekey on Rekeyable Object Provider(s)");
                throw new Exception("Error executing Rekey on Rekeyable Object Provider(s)");
            }


            log.LogInformation("Committing {0} Keys to {1} Application Lifecycle Providers...", generatedSecrets.Count, alcProviders.Count);
            try
            {
                await Task.WhenAll(alcProviders.Select(alcProvider => alcProvider.CommitNewSecrets(generatedSecrets)));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error executing Commit on Application Lifecycle Provider(s)");
                throw new Exception("Error executing Commit on Application Lifecycle Provider(s)");
            }


            log.LogInformation("Completing post-rekey operations on {0} Application Lifecycle Providers...", alcProviders.Count);
            try
            {
                await Task.WhenAll(alcProviders.Select(alp => alp.AfterRekeying()));
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Error executing AfterRekeying on Application Lifecycle Provider(s)");
                throw new Exception("Error executing AfterRekeying on Application Lifecycle Provider(s)");
            }
        }

        public static IAuthJanitorProvider GetProvider(string name) =>
            (_providerTypes.Any(t => t.Name == name) ? ServiceProvider.GetService(_providerTypes.First(t => t.Name == name)) : null) as IAuthJanitorProvider;

        public static string SHA256HashString(string str)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(str));
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    sb.Append(bytes[i].ToString("x2"));
                }

                return sb.ToString();
            }
        }
    }
}