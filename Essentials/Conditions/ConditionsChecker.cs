using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Essentials.Conditions;
using Sandbox.Game.Entities;
using Torch.Commands;

namespace Essentials.Commands
{
    public static class ConditionsChecker
    {
        private static List<Condition> _conditionLookup;

        public static void Init()
        {
            _conditionLookup = new List<Condition>();
                
            var types = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany((x) =>
                {
                    try
                    {
                        return x.GetTypes();
                    }
                    catch (Exception e) // ignored 
                    {
                        return new Type[0];
                    }
                }).Where(t => t.IsDefined(typeof(ConditionModule)));

            foreach (var type in types)
            {
                var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);
                foreach (var m in methods)
                {
                    var a = m.GetCustomAttribute<ConditionAttribute>();
                    if (a == null)
                        continue;

                    var c = new Condition(m, a);

                    _conditionLookup.Add(c);
                }
            }
        }

        public static List<Condition> GetAllConditions()
        {
            return _conditionLookup;
        }
        
        /// <summary>
        /// Returns false after a single error reply if the conditions are wrong. No grids are scanned then.
        /// </summary>
        public static bool TryScanConditions(CommandContext context, IReadOnlyList<string> args, out List<MyCubeGrid> grids)
        {
            grids = null;
            var conditions = new List<Func<MyCubeGrid, bool>>();

            for (var i = 0; i < args.Count; i++)
            {
                string parameter;
                if (i + 1 >= args.Count)
                {
                    parameter = null;
                }
                else
                {
                    parameter = args[i + 1];
                }

                var arg = args[i];

                if (parameter != null)
                {
                    //parameter is the name of a command. Assume this command requires no parameters
                    if (_conditionLookup.Any(c => c.Matches(parameter)))
                    {
                        parameter = null;
                    }
                    //next string is a parameter, so pass it to the condition and skip it next loop
                    else
                        i++;
                }

                var condition = _conditionLookup.FirstOrDefault(c => c.Matches(arg));
                if (condition == null)
                {
                    context.Respond($"Неизвестный аргумент '{arg}'");
                    return false;
                }

                var invert = !arg.Equals(condition.Command, StringComparison.CurrentCultureIgnoreCase);
                var bound = condition.Bind(parameter, invert, out var error);
                if (bound == null)
                {
                    context.Respond(error);
                    return false;
                }

                conditions.Add(bound);
            }

            //default scan to find grids without pilots
            if (!args.Contains("haspilot", StringComparer.CurrentCultureIgnoreCase))
                conditions.Add(g => !ConditionsImplementations.Piloted(g));


            var resultList = new List<MyCubeGrid>();
            Parallel.ForEach(MyCubeGridGroups.Static.Logical.Groups, (group) =>
            {
                foreach (var node in group.Nodes)
                {
                    if (node.NodeData.Projector != null)
                        continue;

                    foreach (var c in conditions)
                    {
                        if (!c(node.NodeData))
                            return;
                    }
                }

                lock (resultList)
                {
                    resultList.AddRange(group.Nodes.Where(x => x.NodeData.Projector == null).Select(x => x.NodeData));
                }
            });

            grids = resultList;
            return true;
        }
        
    }
}