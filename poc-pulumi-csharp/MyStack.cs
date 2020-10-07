using System.Collections.Generic;
using Pulumi;
using Pulumi.Azure.ContainerService;
using Pulumi.Azure.ContainerService.Inputs;
using Pulumi.Azure.Core;
using Pulumi.Azure.KeyVault;
using Pulumi.Azure.KeyVault.Inputs;
using Pulumi.Azure.Network;
using Pulumi.Azure.Network.Inputs;
using Pulumi.Azure.Sql;
using Pulumi.Azure.Sql.Inputs;
using Pulumi.Azure.Storage;
using Database = Pulumi.Azure.Sql.Database;
using DatabaseArgs = Pulumi.Azure.Sql.DatabaseArgs;

class MyStack : Stack
{
    public MyStack()
    {
        var config =  new Config();
        // Create a dict of tags, which can be 
        // used across resources in the stack
        var tag = new Dictionary<string,string>
        {
            {"owner", "midhunmohan3009@gmail.com"},
            {"environment", config.Require("env")},
            {"personal-data", "no"},
            {"confidentiality", "internal"},
            {"last-reviewed", "2020-09-18"}
        };
        
        // Fetch vnet Address Space from stack  config value
        string vnetAddressSpace = config.Require("vnetAddressSpace");
        
        // Fetch Tenant ID from stack config which is set as secret
        var tenantID = config.RequireSecret("tenantId");
        
        // Create a Resource Group
        var resourceGroup = new ResourceGroup("resourceGroup", new ResourceGroupArgs
        {
            Name = "midhun-poc-pulumi",
            Tags = tag
        });
        
        // Create a storage Account
        var storageAccount =  new Account("storage",  new AccountArgs
        {
            Name = "midhunpocpulumi",
            Location = resourceGroup.Location,
            AccountReplicationType = "LRS",
            AccountTier = "Premium",
            ResourceGroupName = resourceGroup.Name,
            Tags = tag
        });
        
        // Create a Keyvault and set access policies
        var keyvault = new KeyVault( "vault", new KeyVaultArgs
        {
            Name = "midhun-poc-pulumi-vault",
            ResourceGroupName = resourceGroup.Name,
            Tags = tag,
            SkuName = "standard",
            Location = resourceGroup.Location,
            TenantId = tenantID,
            AccessPolicies = new KeyVaultAccessPolicyArgs
            {
                SecretPermissions = {"get", "list", "set", "delete"},
                ObjectId = config.Require("ownerAdObjectId"),
                TenantId = tenantID,
            }
        });

        // Create a secret in the above KeyVault
        var secret = new Secret("secret", new SecretArgs
        {
            Name = "storageAccountKey",
            Tags = tag,
            Value = storageAccount.PrimaryConnectionString,
            KeyVaultId = keyvault.Id
        });
        
        // Create a NSG to be used with subnets
        var nsg =  new NetworkSecurityGroup("nsg", new NetworkSecurityGroupArgs
        {
            Name = "midhun-poc-pulumi-nsg",
            Location = resourceGroup.Location,
            Tags = tag,
            ResourceGroupName = resourceGroup.Name,
            SecurityRules = new NetworkSecurityGroupSecurityRuleArgs
            {
                Name = "inbound-443-100",
                Description = "Test NSG",
                Access = "Allow",
                Direction = "Inbound",
                Priority = 100,
                Protocol = "TCP",
                DestinationAddressPrefix = vnetAddressSpace,
                DestinationPortRange = "*",
                SourcePortRange = "443",
                SourceAddressPrefix = "*"
            }
        });
        
        // Create a virtual Network
        // Create subnets in the above vnet
        // Associate NSG created above to the Subnet
        var virtualNetwork = new VirtualNetwork("vNet", new VirtualNetworkArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Tags = tag,
            AddressSpaces = vnetAddressSpace,
            Name = "midhun-poc-pulumi-vnet",
            Subnets =
            {
                new VirtualNetworkSubnetArgs
                {
                    Name = "subnet1",
                    AddressPrefix = "10.198.10.0/29",
                    SecurityGroup = nsg.Id
                },
                new VirtualNetworkSubnetArgs
                {
                    Name = "subnet2",
                    AddressPrefix = "10.198.10.8/29",
                    SecurityGroup = nsg.Id
                }
            }
        });

        // Create two subnets in the above vnet as a standalone resource
        var subnet = new Subnet("sNet", new SubnetArgs
        {
            Name = "subnet3",
            AddressPrefixes = {"10.198.10.16/28"},
            ServiceEndpoints = {"Microsoft.Sql"},
            VirtualNetworkName = virtualNetwork.Name,
            ResourceGroupName = resourceGroup.Name
        });
        
        var subnet4 = new Subnet("sNet1", new SubnetArgs
        {
            Name = "subnet4",
            AddressPrefixes = {"10.198.10.64/26"},
            ServiceEndpoints = {"Microsoft.Sql"},
            VirtualNetworkName = virtualNetwork.Name,
            ResourceGroupName = resourceGroup.Name
        });
        
        // Create second vnet for implementing peering
        var virtualNetwork2 = new VirtualNetwork("vNet2", new VirtualNetworkArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Tags = tag,
            AddressSpaces = "10.197.10.0/24",
            Name = "midhun-poc-pulumi-vnet2",
            Subnets =
            {
                new VirtualNetworkSubnetArgs
                {
                    Name = "subnet21",
                    AddressPrefix = "10.197.10.0/29",
                    SecurityGroup = nsg.Id
                },
                new VirtualNetworkSubnetArgs
                {
                    Name = "subnet22",
                    AddressPrefix = "10.197.10.8/29",
                    SecurityGroup = nsg.Id
                }
            }
        });

        // Find the subnet id of the above created subnet as it is not accessible directly
        var gsubnet1 = Output.Create(GetSubnet.InvokeAsync(new GetSubnetArgs
        {
            Name = "subnet21",
            ResourceGroupName = "midhun-poc-pulumi",
            VirtualNetworkName = "midhun-poc-pulumi-vnet2"
        }));

        this.subnetId = gsubnet1.Apply(gsubnet1 => gsubnet1.Id);

        // Create peering vnet1 --> vnet2
        var peering1 =  new VirtualNetworkPeering("peer1", new VirtualNetworkPeeringArgs
        {
            Name = "peer-vnet1-vnet2",
            AllowGatewayTransit = false,
            ResourceGroupName = resourceGroup.Name,
            VirtualNetworkName = virtualNetwork.Name,
            RemoteVirtualNetworkId = virtualNetwork2.Id
        });

        // Create Reverse Peering vnet2 --> vnet1
        var peering2 =  new VirtualNetworkPeering("peer2", new VirtualNetworkPeeringArgs
        {
            Name = "peer-vnet1-vnet2",
            AllowGatewayTransit = false,
            ResourceGroupName = resourceGroup.Name,
            VirtualNetworkName = virtualNetwork2.Name,
            RemoteVirtualNetworkId = virtualNetwork.Id
        });

        // Create a Nic with dynamic allocation of ip from subnet3
        var networkInterface = new NetworkInterface("nic", new NetworkInterfaceArgs
        {
            Name = "midhun-poc-pulumi-vm-nic",
            Tags = tag,
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            IpConfigurations = new NetworkInterfaceIpConfigurationArgs
            {
                Name = "midhun-poc-pulumi-nic-subnet-association",
                SubnetId = subnet.Id,
                PrivateIpAddressAllocation = "dynamic"
            }
        });
        
        // Create a new Nic to check whether subnet Variable is working as expected
        var networkInterface2 = new NetworkInterface("nic2", new NetworkInterfaceArgs
        {
            Name = "midhun-poc-pulumi-vm-nic2",
            Tags = tag,
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            IpConfigurations = new NetworkInterfaceIpConfigurationArgs
            {
                Name = "midhun-poc-pulumi-nic-subnet-association2",
                SubnetId = subnet4.Id,
                PrivateIpAddressAllocation = "dynamic"
            }
        });
        
        
        // Public IP prefix to be used accross multiple Appliances
        var prefix =  new PublicIpPrefix("prefix", new PublicIpPrefixArgs
        {
            ResourceGroupName = resourceGroup.Name,
            Location = resourceGroup.Location,
            Name = "midhun-poc-pulumi-prefix",
            Sku = "Standard",
            Tags = tag,
            PrefixLength = 28
        });
        
        // Create an aks cluster with nodes in subnet4
        // Nodepool with node count = 1
        // Autoscaling =  true
        // Use system assigned identity
        // use network plugin = AzureCNI
        // k8s version = 1.17.11
        // Use Direct Active directory Integration
        // RBAC =  enabled 
        // setting above two after creating cluster will recreate the cluster :scream:
        // Do not do it in PROD. Action is destroy and create
        // Outbound IP from an already created Public IP prefix
        var akscluster =  new KubernetesCluster("aks", new KubernetesClusterArgs
        {
            Location = resourceGroup.Location,
            Name = "midhun-poc-pulumi-aks-cluster",
            Tags = tag,
            KubernetesVersion = "1.17.11",
            ResourceGroupName = resourceGroup.Name,
            DefaultNodePool = new KubernetesClusterDefaultNodePoolArgs
            {
                Name = "pocpulpool",
                Tags = tag,
                NodeCount = 1,
                OrchestratorVersion = "1.17.11",
                VnetSubnetId = subnet4.Id,
                VmSize = "Standard_DS2_V2",
                OsDiskSizeGb = 500,
                MaxPods = 30,
                EnableAutoScaling = true,
                MaxCount = 3,
                MinCount = 1
            },
            DnsPrefix = "midhun-poc-pulumi-aks-cluster",
            NetworkProfile = new KubernetesClusterNetworkProfileArgs
            {
                NetworkPlugin = "azure",
                LoadBalancerProfile = new KubernetesClusterNetworkProfileLoadBalancerProfileArgs
                {
                    OutboundIpPrefixIds = prefix.Id,
                    IdleTimeoutInMinutes = 5,
                }
            },
            Identity = new KubernetesClusterIdentityArgs
            {
                Type = "SystemAssigned",
            },
            RoleBasedAccessControl = new KubernetesClusterRoleBasedAccessControlArgs
            {
                Enabled = true,
                AzureActiveDirectory = new KubernetesClusterRoleBasedAccessControlAzureActiveDirectoryArgs
                {
                    TenantId = tenantID,
                    Managed = true,
                    AdminGroupObjectIds = config.Require("ownerGroupObjectId")
                }
            }
        });
        
        // Create a sql server        
        var sqlsrv = new SqlServer("sqlsrv", new SqlServerArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Name = "midhun-poc-pulumi-sql-srv",
            Tags = tag,
            Version = "12.0",
            AdministratorLogin = "midhun-poc-admin",
            AdministratorLoginPassword = config.RequireSecret("sqlServerAdministratorPassword"),
            Identity = new SqlServerIdentityArgs
            {
                Type = "SystemAssigned"
            }
        });
        
        // Create a sql db in the above created server
        var sqldb = new Database("sqldb", new DatabaseArgs
        {
            Location = resourceGroup.Location,
            ResourceGroupName = resourceGroup.Name,
            Name = "midhun-poc-pulumi-db",
            Tags = tag,
            ServerName = sqlsrv.Name,
            Edition = "Basic",
            ReadScale = false,
            ZoneRedundant = false,
            MaxSizeGb = "100",
            Collation = "Norwegian_100_CI_AS",
        });

       
    }

    [Output]
    public Output<string> subnetId { get; set; }
    

}
