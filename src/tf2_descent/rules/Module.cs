using TF2.Descent.Rules;
using SourceSharp.Host.Contracts;

// The entry the hosting service instantiates in the module's own load context.
[assembly: HostRulesModule(typeof(TF2DescentRules))]
