using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace mAIx.Converters
{
    /// <summary>
    /// IsAddedToTodo 상태에 따라 To Do 아이콘을 반환하는 컨버터
    /// True: Checkmark24 (체크표시), False: TaskListAdd24 (추가 아이콘)
    /// </summary>
    public class BoolToTodoIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAdded && isAdded)
            {
                return SymbolRegular.Checkmark24;  // 추가됨 상태
            }
            return SymbolRegular.TaskListAdd24;  // 추가 가능 상태
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// IsAddedToTodo 상태에 따라 툴팁을 반환하는 컨버터
    /// </summary>
    public class BoolToTodoTooltipConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isAdded && isAdded)
            {
                return "To Do에서 제거";
            }
            return "To Do에 추가";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
