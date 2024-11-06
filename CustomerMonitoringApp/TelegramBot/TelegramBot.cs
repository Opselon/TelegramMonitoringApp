using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Telegram.Bot;

namespace CustomerMonitoringApp.TelegramBot
{
    public class TelegramBot
    {
        private readonly ITelegramBotClient _botClient;

        public TelegramBot(string token)
        {
            _botClient = new TelegramBotClient(token);
        }

        public ITelegramBotClient GetClient() => _botClient;
    }
}