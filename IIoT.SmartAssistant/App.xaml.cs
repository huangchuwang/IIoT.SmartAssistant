using System.Windows;
using Prism.Ioc;
using Prism.DryIoc;
using Prism.Mvvm;
using IIoT.SmartAssistant.ViewModels;
using IIoT.SmartAssistant.Views;

namespace IIoT.SmartAssistant
{
    public partial class App : PrismApplication
    {
        //指定程序启动时显示的第一个主窗口
        protected override Window CreateShell()
        {
            return Container.Resolve<MainView>();
        }

        //依赖注入 (DI) 容器注册
        protected override void RegisterTypes(IContainerRegistry containerRegistry)
        {

        }

        //配置 View 和 ViewModel 的绑定关系
        // (当Window 叫 MainWindow，而 ViewModel 叫 MainViewModel，打破了 Prism 默认的命名约定，所以这里手动指定映射)
        protected override void ConfigureViewModelLocator()
        {
            base.ConfigureViewModelLocator();
            //ViewModelLocationProvider.Register<MainView, MainViewModel>();
        }
    }
}