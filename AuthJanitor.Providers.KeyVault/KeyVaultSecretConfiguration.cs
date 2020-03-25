﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace AuthJanitor.Providers.KeyVault
{
    public class KeyVaultSecretConfiguration : AuthJanitorProviderConfiguration
    {
        public const int DEFAULT_SECRET_LENGTH = 64;

        /// <summary>
        /// Key Vault name (xxxxx.vault.azure.net)
        /// </summary>
        [Description("Vault Name")]
        public string VaultName { get; set; }

        /// <summary>
        /// Name Secret being operated upon
        /// </summary>
        [Description("Secret Name")]
        public string SecretName { get; set; }

        /// <summary>
        /// Length of secret to regenerate
        /// </summary>
        [Description("New Secret Length")]
        public int SecretLength { get; set; } = DEFAULT_SECRET_LENGTH;
    }
}
