using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace VRC.OSCQuery
{
    public class OSCQueryServiceProfile : IEquatable<OSCQueryServiceProfile>
    {
        public int Port { get; set; }
        public string Name { get; set; }
        public IPAddress[] Addresses { get; set; }
        public ProfileServiceType ServiceType { get; set; }

        public enum ProfileServiceType
        {
            Unknown, OSCQuery, OSC
        }

        public string GetServiceTypeString()
        {
            switch (ServiceType)
            {
                case ProfileServiceType.OSC:
                    return Attributes.SERVICE_OSC_UDP;
                case ProfileServiceType.OSCQuery:
                    return Attributes.SERVICE_OSCJSON_TCP;
                default:
                    return "UNKNOWN";
            }
        }

        public OSCQueryServiceProfile(string name, IEnumerable<IPAddress> addresses, int port, ProfileServiceType serviceType)
        {
            Name = name;
            Addresses = addresses.Distinct().ToArray();
            Port = port;
            ServiceType = serviceType;
        }

        public bool Equals(OSCQueryServiceProfile other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Port == other.Port && Name == other.Name && Equals(Addresses, other.Addresses) && ServiceType == other.ServiceType;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != this.GetType()) return false;
            return Equals((OSCQueryServiceProfile)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Port;
                hashCode = (hashCode * 397) ^ (Name != null ? Name.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Addresses != null ? Addresses.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)ServiceType;
                return hashCode;
            }
        }
    }
}
