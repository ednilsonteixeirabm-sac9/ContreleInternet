using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace ControleInternet.Common
{
    [DataContract]
    public sealed class AppConfig
    {
        public AppConfig()
        {
            AllowedDomains = new List<string>();
            PasswordIterations = PasswordHasher.DefaultIterations;
        }

        [DataMember(Name = "blockAllSites", Order = 1)]
        public bool BlockAllSites { get; set; }

        [DataMember(Name = "allowListedSites", Order = 2)]
        public bool AllowListedSites { get; set; }

        [DataMember(Name = "allowedDomains", Order = 3)]
        public List<string> AllowedDomains { get; set; }

        [DataMember(Name = "passwordSalt", Order = 4)]
        public string PasswordSalt { get; set; }

        [DataMember(Name = "passwordHash", Order = 5)]
        public string PasswordHash { get; set; }

        [DataMember(Name = "passwordIterations", Order = 6)]
        public int PasswordIterations { get; set; }

        [IgnoreDataMember]
        public bool HasPassword
        {
            get
            {
                return !string.IsNullOrEmpty(PasswordSalt)
                    && !string.IsNullOrEmpty(PasswordHash)
                    && PasswordIterations > 0;
            }
        }

        public AppConfig Clone()
        {
            return new AppConfig
            {
                BlockAllSites = BlockAllSites,
                AllowListedSites = AllowListedSites,
                AllowedDomains = AllowedDomains == null
                    ? new List<string>()
                    : new List<string>(AllowedDomains),
                PasswordSalt = PasswordSalt,
                PasswordHash = PasswordHash,
                PasswordIterations = PasswordIterations
            };
        }

        public void Normalize()
        {
            IEnumerable<string> domains = AllowedDomains ?? Enumerable.Empty<string>();
            AllowedDomains = domains
                .Select(DomainName.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (PasswordIterations <= 0)
            {
                PasswordIterations = PasswordHasher.DefaultIterations;
            }
        }

        public AppConfig WithoutPassword()
        {
            AppConfig copy = Clone();
            copy.PasswordSalt = null;
            copy.PasswordHash = null;
            copy.PasswordIterations = 0;
            return copy;
        }
    }
}
