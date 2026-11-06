using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;
using Sandbox.Game.Screens.Helpers;
using Torch;
using Torch.Views;
using VRage;
using VRage.Game;
using VRage.ObjectBuilders;

namespace Essentials
{
    public class EssentialsConfig : ViewModel
    {
        public EssentialsConfig()
        {
            AutoCommands.CollectionChanged += (sender, args) => OnPropertyChanged();
            InfoCommands.CollectionChanged += (sender, args) => OnPropertyChanged();
        }

        [Display(Order = 30, GroupName = "Автокоманды", EditorType = typeof(EmbeddedCollectionEditor), Description = "Команды, которые сервер выполняет сам: по времени, по расписанию, при старте, по условию или по итогам голосования !vote.")]
        public ObservableCollection<AutoCommand> AutoCommands { get; } = new ObservableCollection<AutoCommand>();

        [Display(Order = 40, GroupName = "Инфо-команды", EditorType = typeof(EmbeddedCollectionEditor), Description = "Сообщения чата, на которые сервер отвечает игроку заданным текстом в чат, окном или ссылкой. Изменения действуют сразу.")]
        public ObservableCollection<InfoCommand> InfoCommands { get; } = new ObservableCollection<InfoCommand>();

        private string _motd;
        [Display(Order = 1, Name = "Сообщение дня", GroupName = "Общие/Сообщение дня", Description = "Окно, которое игрок видит после входа на сервер. %player% заменяется на ник игрока.")]
        [Multiline]
        public string Motd { get => _motd; set => SetValue(ref _motd, value); }

        public bool _enableRanks = false;
        [Display(Order = 50, Name = "Включить ранги", GroupName = "Ранги", Description = "Включает систему рангов Essentials (команды !pr). Действует после перезапуска сервера.")]
        public bool EnableRanks { get => _enableRanks; set => SetValue(ref _enableRanks, value); }

        public string _defaultRank = "Default";
        [Display(Order = 51, Name = "Ранг по умолчанию", GroupName = "Ранги", Description = "Ранг, который игрок получает при первом входе на сервер.")]
        public string DefaultRank { get => _defaultRank; set => SetValue(ref _defaultRank, value); }

        public bool _overridePerms = false;
        [Display(Order = 52, Name = "Права рангов вместо прав Torch", GroupName = "Ранги", Description = "Права из рангов заменяют обычные права Torch и плагинов, а уровень прав игрока при входе берётся из его ранга. Перед включением задайте рангам права!")]
        public bool OverrideVanillaPerms { get => _overridePerms; set => SetValue(ref _overridePerms, value); }

        public bool _enableHomes = false;
        [Display(Order = 53, Name = "Включить дома", GroupName = "Ранги", Description = "Включает команды !home. Сколько домов можно сохранить, задаёт ранг игрока (!pr setmaxhomes).")]
        public bool EnableHomes { get => _enableHomes; set => SetValue(ref _enableHomes, value); }

        private string _newUserMotd;
        [Display(Order = 2, Name = "Сообщение для новичков", GroupName = "Общие/Сообщение дня", Description = "Показывается вместо сообщения дня игроку, который заходит на сервер впервые. %player% заменяется на ник игрока.")]
        [Multiline]
        public string NewUserMotd { get => _newUserMotd; set => SetValue(ref _newUserMotd, value); }

        private string _motdUrl;
        [Display(Order = 3, Name = "Ссылка вместо сообщения", GroupName = "Общие/Сообщение дня", Description = "Если задана, после входа у игрока открывается эта страница в оверлее Steam вместо сообщения дня.")]
        public string MotdUrl { get => _motdUrl; set => SetValue(ref _motdUrl, value); }

        private bool _newUserMotdUrl;
        [Display(Order = 4, Name = "Ссылка только новичкам", GroupName = "Общие/Сообщение дня", Description = "Открывать ссылку только тем, кто заходит впервые. Остальные видят сообщение дня.")]
        public bool NewUserMotdUrl{get => _newUserMotdUrl;set => SetValue(ref _newUserMotdUrl, value);}

        private bool _stopShips;
        [Display(Order = 10, Name = "Остановить всё при старте", GroupName = "Общие/Сервер", Description = "При запуске сервера обнуляет скорость всех объектов мира.")]
        public bool StopShipsOnStart { get => _stopShips; set => SetValue(ref _stopShips, value); }


        private bool _utilityShowPosition;

        [Display(Order = 21, Name = "Координаты в !grids list", GroupName = "Общие/Игроки", Description = "Показывать игрокам координаты их гридов в !grids list.")]
        public bool UtilityShowPosition
        {
            get => _utilityShowPosition;
            set => SetValue(ref _utilityShowPosition, value);
        }

        private bool _markerShowPosition;

        [Display(Order = 22, Name = "GPS-метки в !grids list", GroupName = "Общие/Игроки", Description = "Ставить игрокам GPS-метки на их гриды при !grids list.")]
        public bool MarkerShowPosition
        {
            get => _markerShowPosition;
            set => SetValue(ref _markerShowPosition, value);
        }

        private int _backpackLimit = 1;
        [Display(Order = 20, Name = "Лимит рюкзаков", GroupName = "Общие/Игроки", Description = "Сколько рюкзаков может быть у одного игрока. Пустые и лишние рюкзаки удаляются через 30 секунд, лишние начиная с самых старых. 0 — удалять все, -1 — без ограничения.")]
        public int BackpackLimit
        {
            get => _backpackLimit;
            set => SetValue(ref _backpackLimit, value);
        }

        private bool _cutGameTags;
        [Display(Order = 11, Name = "Скрыть моды и лимиты в списке серверов", GroupName = "Общие/Сервер", Description = "Убирает моды и лимиты блоков из информации о сервере в списке серверов. Помогает от ошибки «error downloading session settings» при подключении. Действует после перезапуска Torch.")]
        public bool CutGameTags
        {
            get => _cutGameTags;
            set => SetValue(ref _cutGameTags, value);
        }

        private MyObjectBuilder_Toolbar _vanillaBacking;

        [XmlIgnore]
        private MyObjectBuilder_Toolbar VanillaDefaultToolbar => _vanillaBacking ?? (_vanillaBacking = new MyToolbar(MyToolbarType.Character, 9, 9).GetObjectBuilder());

        private MyObjectBuilder_Toolbar _defaultToolbar;

        [Display(Visible=false)]
        //TODO!
        public ToolbarWrapper DefaultToolbar
        {
            get => _defaultToolbar ?? VanillaDefaultToolbar;
            set
            {
                bool valueChanged = false;

                if (value.Data.Slots.Count == VanillaDefaultToolbar.Slots.Count)
                {
                    for (int i = 0; i < value.Data.Slots.Count; i++)
                    {
                        var val = value.Data.Slots[i];
                        var van = VanillaDefaultToolbar.Slots[i];
                        if (val.Index != van.Index || val.Data.SubtypeId != van.Data.SubtypeId)
                        {
                            valueChanged = true;
                            break;
                        }
                    }
                }
                
                if (valueChanged)
                    SetValue(ref _defaultToolbar, value);
            }
        }

        public bool ShouldSerializeDefaultToolbar()
        {
            return _defaultToolbar != null;
        }

        /// <summary>
        /// Allows us to use Keen's serializer without losing previously stored config data
        /// </summary>
        public class ToolbarWrapper : IXmlSerializable
        {
            public MyObjectBuilder_Toolbar Data { get; set; }

            public XmlSchema GetSchema()
            {
                return null;
            }

            public void ReadXml(XmlReader reader)
            {
                var ser = MyXmlSerializerManager.GetSerializer(typeof(MyObjectBuilder_Toolbar));
                var o = ser.Deserialize(reader);
                Data = (MyObjectBuilder_Toolbar)o;
            }

            public void WriteXml(XmlWriter writer)
            {
                var ser = MyXmlSerializerManager.GetSerializer(typeof(MyObjectBuilder_Toolbar));
                ser.Serialize(writer, Data);
            }

            public static implicit operator MyObjectBuilder_Toolbar(ToolbarWrapper o)
            {
                return o.Data;
            }

            public static implicit operator ToolbarWrapper(MyObjectBuilder_Toolbar o)
            {
                return new ToolbarWrapper(){Data = o};
            }
        }
    }
}
