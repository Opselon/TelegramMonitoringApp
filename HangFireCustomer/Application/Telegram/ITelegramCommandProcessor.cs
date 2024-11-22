namespace HangFireCustomer.Application.Telegram
{
    public interface ITelegramCommandProcessor
    {
        void EnqueueCommand(string commandText, long chatId);
    }
}