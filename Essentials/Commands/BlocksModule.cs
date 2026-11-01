using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using VRage.Game.ModAPI;
using Sandbox.Game.Entities.Cube;
using Sandbox.ModAPI;
using Torch.Commands;

namespace Essentials.Commands
{

    [Category("blocks")]
    public class BlocksModule : CommandModule
    {
        [Command("on type", "Включает все блоки указанного типа.")]
        public void OnType(string type)
        {
            var count = 0;
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
                    if (block != null && string.Compare(type, blockType, StringComparison.InvariantCultureIgnoreCase) ==
                        0)
                    {
                        block.Enabled = true;
                        count++;
                    }
                }
            }

            Context.Respond($"Включено блоков типа {type}: {count}.");
        }

        [Command("on subtype", "Включает все блоки указанного подтипа.")]
        public void OnSubtype(string subtype)
        {
            var count = 0;
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockType = block.BlockDefinition.Id.SubtypeName;
                    if (block != null &&
                        string.Compare(subtype, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        block.Enabled = true;
                        count++;
                    }
                }
            }

            Context.Respond($"Включено блоков подтипа {subtype}: {count}.");
        }

        [Command("off type", "Выключает все блоки указанного типа.")]
        public void OffType(string type)
        {
            var count = 0;
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
                    if (block != null && string.Compare(type, blockType, StringComparison.InvariantCultureIgnoreCase) == 0 && block.Enabled == true)
                    {
                        block.Enabled = false;
                        count++;
                    }
                }
            }


            Context.Respond($"Выключено блоков типа {type}: {count}.");
        }

        [Command("remove subtype", "Удаляет все блоки указанного подтипа.")]
        public void RemoveSubtype(string subtype)
        {
            var toRemove = new List<MySlimBlock>();
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetBlocks())
                {
                    var blockType = block.BlockDefinition.Id.SubtypeName;
                    if (string.Compare(subtype, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                        toRemove.Add(block);
                }
            }

            foreach (var x in toRemove)
                x.CubeGrid?.RemoveBlock(x);
            Context.Respond($"Удалено блоков подтипа {subtype}: {toRemove.Count}.");
        }

        [Command("remove type", "Удаляет все блоки указанного типа.")]
        public void RemoveType(string type)
        {
            var toRemove = new List<MySlimBlock>();
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetBlocks())
                {
                    var blockType = block.BlockDefinition.Id.TypeId.ToString().Substring(16);
                    if (string.Compare(type, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                        toRemove.Add(block);
                }
            }

            foreach (var x in toRemove)
                x.CubeGrid?.RemoveBlock(x);
            Context.Respond($"Удалено блоков типа {type}: {toRemove.Count}.");
        }

        [Command("off subtype", "Выключает все блоки указанного подтипа.")]
        public void OffSubtype(string subtype)
        {
            var count = 0;
            foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
            {
                foreach (var block in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                {
                    var blockType = block.BlockDefinition.Id.SubtypeName;
                    if (block != null &&
                        string.Compare(subtype, blockType, StringComparison.InvariantCultureIgnoreCase) == 0)
                    {
                        block.Enabled = false;
                        count++;
                    }
                }
            }


            Context.Respond($"Выключено блоков подтипа {subtype}: {count}.");
        }

        [Command("on general", "Включает все блоки указанной категории")]
        public void OnGeneral(string category)
        {
            var count = 0;
            if (Enum.TryParse(category, out BlockCategory result))
            {
                foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
                {
                    foreach (var item in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                    {
                        if (IsBlockTypeOf(item, result))
                        {

                            item.Enabled = true;
                            count++;
                        }
                    }
                }


            }
            else
            {
                Context.Respond($"Неверная категория {category}. Используйте одну из следующих: " + string.Join(", ", Enum.GetValues(typeof(BlockCategory))));
                return;
            }



            Context.Respond($"Включено блоков категории {category}: {count}.");
        }


        [Command("off general", "Выключает все блоки указанной категории")]
        public void OffGeneral(string category)
        {
            var count = 0;

            if (Enum.TryParse(category, out BlockCategory result))
            {
                foreach (var grid in MyEntities.GetEntities().OfType<MyCubeGrid>().Where(x => x.Projector == null))
                {
                    foreach (var item in grid.GetFatBlocks().OfType<MyFunctionalBlock>())
                    {
                        if (IsBlockTypeOf(item, result))
                        {

                            item.Enabled = false;
                            count++;
                        }
                    }
                }


            }
            else
            {
                Context.Respond($"Неверная категория {category}. Используйте одну из следующих: " + string.Join(", ", Enum.GetValues(typeof(BlockCategory))));
                return;
            }



            Context.Respond($"Выключено блоков категории {category}: {count}.");
        }


        public bool IsBlockTypeOf(MyFunctionalBlock block, BlockCategory category)
        {
            switch (category)
            {
                case BlockCategory.Power:
                    return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Reactor) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_BatteryBlock) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_SolarPanel)  ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_FueledPowerProducer);
                case BlockCategory.Production:
                    return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Assembler) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_Refinery) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_OxygenGenerator);
                case BlockCategory.Weapons:
                    return block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_InteriorTurret) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_LargeGatlingTurret) ||
                           block.BlockDefinition.Id.TypeId == typeof(MyObjectBuilder_LargeMissileTurret);
                default:
                    throw new InvalidBranchException();
            }
        }

        public enum BlockCategory
        {
            Power,
            Production,
            Weapons

        }
    }

}