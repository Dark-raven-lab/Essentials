using System.Runtime.CompilerServices;
using Torch;
using Torch.Views;

namespace Essentials
{
    public class InfoCommand : ViewModel
    {
        private string _command;
        private string _chatResponse;
        private string _dialogResponse;
        private string _urlResponse;

        [Display(Order = 1, Name = "Команда", Description = "Сообщение чата целиком, на которое отвечает сервер, например !discord. Само сообщение в чат не попадает.")]
        public string Command
        {
            get => _command;
            set => SetValue(ref _command, value);
        }

        [Display(Order = 2, Name = "Ответ в чат", Description = "Текст, который игрок получит в чат. Пусто — без ответа в чат.")]
        public string ChatResponse
        {
            get => _chatResponse;
            set =>SetValue(ref _chatResponse, value);
        }

        [Display(Order = 3, Name = "Ответ окном", Description = "Текст, который откроется у игрока в окне. Пусто — без окна.")]
        public string DialogResponse
        {
            get => _dialogResponse;
            set => SetValue(ref _dialogResponse, value);
        }

        [Display(Order = 4, Name = "Ссылка", Description = "Страница, которая откроется у игрока в оверлее Steam. Пусто — без ссылки.")]
        public string URL
        {
            get => _urlResponse;
            set => SetValue(ref _urlResponse, value);
        }
        
        public override string ToString()
        {
            return Command;
        }
    }
}
