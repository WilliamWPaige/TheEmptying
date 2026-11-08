using System.Collections.Generic;
using TheEmptying.Core.Content;
using TheEmptying.Core.Randomness;

namespace TheEmptying.Core.Rules
{
    /// <summary>
    /// IRuleNodeContext for one node's step. Its value cache is scoped to this
    /// one instance (created fresh per node execution by RuleRunner.RunFrom),
    /// so a value node wired into more than one input of the same step
    /// computes only once.
    /// </summary>
    internal sealed class RuleStepContext : IRuleNodeContext
    {
        private readonly RuleRunner _runner;
        private readonly RuleInstance _instance;
        private readonly ActivatedRule _activated;
        private readonly RuleStepsState _steps;
        private readonly Dictionary<string, RuleValue> _valueCache = new Dictionary<string, RuleValue>();

        private string _currentNodeId;

        public RuleStepContext(RuleRunner runner, RuleInstance instance, ActivatedRule activated, string currentNodeId, RuleStepsState steps)
        {
            _runner = runner;
            _instance = instance;
            _activated = activated;
            _currentNodeId = currentNodeId;
            _steps = steps;
        }

        public SimRandom Random => _runner.RandomStream;
        public SettingsRegistry Settings => _runner.Settings;

        public RuleValue GetInput(string name)
        {
            CompiledRuleNode node = _activated.Rule.Nodes[_currentNodeId];

            if (_activated.DataInputConnections.TryGetValue((_currentNodeId, name), out CompiledRuleConnection connection))
            {
                return ResolveNodeValue(connection.FromNode);
            }

            if (node.Fields.TryGetValue(name, out CompiledFieldValue field))
            {
                return ResolveField(field);
            }

            PortDefinition port = node.BlockType.FindPort(name, PortDirection.Input);
            return RuleValue.Default(port?.DataType ?? RuleDataType.Number);
        }

        public bool IsInputConnected(string portName)
        {
            return _activated.DataInputConnections.ContainsKey((_currentNodeId, portName));
        }

        public void Log(string message) => _runner.Log(message);

        public void PostEvent(string eventDefinitionId, IReadOnlyList<(string Name, RuleValue Value)> parameters)
        {
            _runner.PostRuleEvent(eventDefinitionId, parameters, _instance);
        }

        public RuleValue GetLocal(string name, RuleDataType type) => _instance.GetLocal(name, type);

        public void SetLocal(string name, RuleDataType type, RuleValue value) => _instance.SetLocal(name, type, value);

        public bool TryGetEventParameter(string name, RuleDataType type, out RuleValue value)
        {
            return _instance.TryGetEventParameter(name, type, out value);
        }

        public void RunExecutionOutput(string portName)
        {
            if (_activated.ExecOutputTargets.TryGetValue((_currentNodeId, portName), out string nextNodeId))
            {
                _runner.RunFrom(nextNodeId, _instance, _activated, _steps);
            }
        }

        private RuleValue ResolveNodeValue(string nodeId)
        {
            if (_valueCache.TryGetValue(nodeId, out RuleValue cached))
            {
                return cached;
            }

            CompiledRuleNode node = _activated.Rule.Nodes[nodeId];
            string previousNodeId = _currentNodeId;
            _currentNodeId = nodeId;

            RuleValue value;
            try
            {
                value = node.BlockType.ComputeValue(this);
            }
            catch (RuleNodeExecutionException)
            {
                throw;
            }
            catch (System.Exception ex)
            {
                throw new RuleNodeExecutionException(nodeId, ex);
            }
            finally
            {
                _currentNodeId = previousNodeId;
            }

            _valueCache[nodeId] = value;
            return value;
        }

        private RuleValue ResolveField(CompiledFieldValue field)
        {
            return RuleFieldResolver.Resolve(field, _runner.Settings);
        }
    }
}
