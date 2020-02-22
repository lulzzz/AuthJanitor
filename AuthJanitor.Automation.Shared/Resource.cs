﻿using System;

namespace AuthJanitor.Automation.Shared
{
    public class Resource
    {
        public Guid ResourceId { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public bool IsRekeyableObjectProvider { get; set; }
        public string ProviderName { get; set; }
        public string ProviderConfiguration { get; set; }
    }
}