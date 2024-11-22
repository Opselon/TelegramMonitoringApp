using System.Threading.Tasks;

namespace HangFireCustomer.Infrastructure.Telegram
{
    public interface ITelegramBotService
    {
        Task ProcessCommandAsync(string commandText, long chatId);
    }
}