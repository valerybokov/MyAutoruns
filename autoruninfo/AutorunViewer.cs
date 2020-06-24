using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using System.Security.AccessControl;
using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using TaskScheduler;
using System.Windows.Media;

namespace AutorunInfo
{
    sealed public class AutorunViewer : IDisposable
    {
        #region fields and static constructor
        private readonly string[] pathesHKLM_ = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnceEx",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunServices",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunServicesOnce",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Classes\AllFileSystemObjects\ShellEx\ContextMenuHandlers",
            @"SOFTWARE\Classes\Directory\shellex",
        };

        private readonly string[] pathesHKCU_ = {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce",
            @"SOFTWARE\Classes\Directory\shellex\ContextMenuHandlers"
        };


        private RegistryKey hklm_;
        /// <summary>
        /// Используется для выполнения комманд PowerShell.
        /// (В данном случае, получение информации о сертификате файла)
        /// </summary>
        private Runspace runspace_;
        //используется для получения иконки файла в UI потоке
        private readonly Action<Info, string> extractIconAndAddItem_;
        private Action<Info> addItem_;

        //буфер для исполнения extractIcon_
        private readonly object[] buffer2_ = new object[2];
        private readonly object[] buffer1_ = new object[1];
        //кэш иконок для файлов, что имеют разные аргументы коммандной строки, но одинаковый адрес
        private readonly Dictionary<string, ImageSource> iconsCache_ =
                                        new Dictionary<string, ImageSource>();
        private readonly static string programFiles = "%ProgramFiles%";
        private readonly static string sysRoot = @"\SystemRoot\", sysRoot2 = @"%SystemRoot%\", localAppData = @"%localappdata%";
        private readonly static string sys32 = "System32";
        private readonly static string windir="%windir%";
        private readonly static string rightProgramFiles;

        private readonly static string rightWinDir;
        private readonly static string rightSystemRoot;
        private readonly static string rightLocalAppData;
        private readonly static System.Windows.Media.Imaging.BitmapSizeOptions emptyOptions;

        static AutorunViewer()
        {
            string temp = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

            if (temp.EndsWith("(x86)"))
                rightProgramFiles = temp.Substring(0, temp.Length - 6);
            else
                rightProgramFiles = temp;

            rightWinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            rightSystemRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + "\\";
            rightLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            emptyOptions = System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions();
        }
        #endregion fields and static constructor

        #region public methods
        public AutorunViewer()
        {
            extractIconAndAddItem_ = (info, path) => {
                info.Icon = ExtractIcon(path);
                addItem_(info);
            };
        }

        /// <summary>
        /// Метод инициализации
        /// </summary>
        public void BeginInitialize()
        {
            hklm_ = RegistryKey.OpenBaseKey(
                RegistryHive.LocalMachine,
                Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);

            var runspaceConfiguration = RunspaceConfiguration.Create();
            runspace_ = RunspaceFactory.CreateRunspace(runspaceConfiguration);
            runspace_.Open();
        }

        public void EndInitialize()
        {
            iconsCache_.Clear();
        }

        public void GetRegistryHKLMInfo(
            ICollection<Info> list, Action<Delegate, object[]> invokeInUIThread)
        {
            if (hklm_ == null || runspace_ == null)
                throw new ObjectDisposedException("Object was not initialized or disposed");

            addItem_ = list.Add;

            for (int i = 0; i < pathesHKLM_.Length; ++i)
            {
                GetInfoFromRegistry(hklm_, pathesHKLM_[i], addItem_, invokeInUIThread);
            }

            addItem_ = null;
        }

        /// <summary>
        /// Возвращает задачи из TaskScheduler
        /// </summary>
        /// <param name="list">Список задач для заполнения</param>
        /// <param name="invokeInUIThread">Делегат, если нужно выполнить операцию в потоке GUI</param>
        public void GetTaskSchedulerInfo(
            ICollection<Info> list, Action<Delegate, object[]> invokeInUIThread)
        {
            if (runspace_ == null)
                throw new ObjectDisposedException("Object was not initialized or disposed");

            var taskService = new TaskScheduler.TaskScheduler();
            ITaskFolder taskFolder;

            try
            {
                taskService.Connect(/*null, null, null, null*/);
                taskFolder = taskService.GetFolder(@"\");
            }
            catch// (Exception r)
            {
                return;
            }

            addItem_ = list.Add;
            // TaskScheduler._TASK_ENUM_FLAGS
            GetTasks(taskFolder, addItem_, invokeInUIThread, 1);

            addItem_ = null;
        }

        public void GetRegistryHKCUInfo(
            ICollection<Info> list, Action<Delegate, object[]> invokeInUIThread)
        {
            if (runspace_ == null)
                throw new ObjectDisposedException("Object was not initialized or disposed");

            addItem_ = list.Add;

            var hkcu_ = RegistryKey.OpenBaseKey(
                    RegistryHive.CurrentUser,
                    //получаем битность ОС и устанавливаем RegistryView
                    Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32);

            for (int i = 0; i < pathesHKCU_.Length; ++i)
            {
                GetInfoFromRegistry(hkcu_, pathesHKCU_[i], addItem_, invokeInUIThread);
            }

            addItem_ = null;
            hkcu_.Dispose();
        }

        public void GetServicesInfo(
            ICollection<Info> list, Action<Delegate, object[]> invokeInUIThread)
        {
            if (hklm_ == null || runspace_ == null)
                throw new ObjectDisposedException("Object was not initialized or disposed");

            var sk = hklm_.OpenSubKey(
                @"System\CurrentControlSet\Services",
                RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey);

            if (sk == null) return;

            addItem_ = list.Add;

            string imagePath;
            RegistryKey sk2;
            Info info;
            foreach (var it in sk.GetSubKeyNames())
            {
                sk2 = sk.OpenSubKey(it, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey);

                if (sk2.GetValue("Start") != null)
                {
                    imagePath = (string)sk2.GetValue("ImagePath");

                    if (imagePath != null)
                    {
                        info = new Info("Registry");
                        imagePath = SetPathInfo(imagePath, info);
                        //если файл системный, то File.Exists может вернуть false
                        // https://docs.microsoft.com/en-us/dotnet/api/system.io.file.getaccesscontrol?view=netframework-4.7.2
                        if (File.Exists(imagePath))
                        {
                            SetSignatureInfo(imagePath, info);

                            buffer2_[0] = info;
                            buffer2_[1] = imagePath;

                            invokeInUIThread(extractIconAndAddItem_, buffer2_);
                        }
                        else
                        {
                            buffer1_[0] = info;
                            invokeInUIThread(addItem_, buffer1_);
                        }

                        imagePath = null;
                    }
                }

                sk2.Dispose();
            }

            sk.Dispose();
            addItem_ = null;
        }

        /// <summary>
        /// Получить информацию из папки автозагрузки
        /// </summary>
        /// <param name="list">Список для записи информации</param>
        /// <param name="invokeInUIThread">Делегат, если нужно выполнить операцию в потоке GUI</param>
        public void GetStartupInfo(
            ICollection<Info> list, Action<Delegate, object[]> invokeInUIThread)
        {
            if (runspace_ == null)
                throw new ObjectDisposedException("Object was not initialized or disposed");

            addItem_ = list.Add;
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            string[] files = Directory.GetFiles(dir, "*.lnk");
            Info info;
            string imagePath;

            foreach (string path in files)
            {
                imagePath = GetShortcutTarget(path);
                info = new Info("Start Menu");
                imagePath = SetPathInfo(imagePath, info);

                //Если файл системный, то File.Exists может вернуть false
                // https://docs.microsoft.com/en-us/dotnet/api/system.io.file.getaccesscontrol?view=netframework-4.7.2
                if (File.Exists(imagePath))
                {
                    SetSignatureInfo(imagePath, info);

                    buffer2_[0] = info;
                    buffer2_[1] = imagePath;

                    invokeInUIThread(extractIconAndAddItem_, buffer2_);
                }
                else
                {
                    buffer1_[0] = info;
                    invokeInUIThread(addItem_, buffer1_);
                }
            }

            addItem_ = null;
        }

        public void Dispose()
        {
            DoDispose();
            GC.SuppressFinalize(this);
        }

        #endregion public methods

        #region private methods
        private void DoDispose()
        {
            if (hklm_ == null) return;

            hklm_.Dispose();
            hklm_ = null;

            runspace_.Dispose();
            runspace_ = null;
        }

        /// <summary>
        /// Заполняет список задач из TaskScheduler
        /// </summary>
        /// <param name="folder">Папка с задачами</param>
        /// <param name="list">Список задач для заполнения</param>
        /// <param name="invokeInUIThread">Делегат, если нужно выполнить операцию в потоке GUI</param>
        /// <param name="includeHiddenFlag">Указывает какие задачи включать в список</param>
        private void GetTasks(
            ITaskFolder folder, Action<Info> addItemAction,
            Action<Delegate, object[]> invokeInUIThread, int includeHiddenFlag)
        {
            string imagePath;
            Info info;
            ITaskDefinition def;
            IActionCollection acs;
            IExecAction ac;

            foreach (IRegisteredTask task in folder.GetTasks(includeHiddenFlag))
            {
                def = task.Definition;
                acs = def.Actions;

                foreach (IAction item in acs)
                {
                    if (item.Type == _TASK_ACTION_TYPE.TASK_ACTION_EXEC)//item is IExecAction
                    {
                        info = new Info("Scheduler");

                        ac = (IExecAction)item;
                        imagePath = SetPathInfo(ac.Path, info);

                        info.CmdArguments = ac.Arguments;
                        // если файл системный, то File.Exists может вернуть false
                        // https://docs.microsoft.com/en-us/dotnet/api/system.io.file.getaccesscontrol?view=netframework-4.7.2
                        if (File.Exists(imagePath))
                        {
                            SetSignatureInfo(imagePath, info);

                            buffer2_[0] = info;
                            buffer2_[1] = imagePath;
                            //NOTE операции получения иконки и добавления в список можно объединить -
                            //обе выполняются в UI- потоке
                            invokeInUIThread(extractIconAndAddItem_, buffer2_);
                        }
                        else
                        {
                            buffer1_[0] = info;
                            invokeInUIThread(addItemAction, buffer1_);
                        }
                    }
                }

                // release COM object
                System.Runtime.InteropServices.Marshal.ReleaseComObject(task);
            }
                
			foreach (ITaskFolder subFolder in folder.GetFolders(0))//param must be zero
            {
                GetTasks(subFolder, addItemAction, invokeInUIThread, includeHiddenFlag);
            }
				
            System.Runtime.InteropServices.Marshal.ReleaseComObject(folder);
        }

        /// <summary>
        /// Возвращает адрес файла по ярлыку (target)
        /// </summary>
        /// <param name="file">Адрес ярлыка</param>
        /// <returns>Адрес файла</returns>
        private string GetShortcutTarget(string file)
        {
            FileStream fileStream = null;

            try
            {
                fileStream = File.Open(file, FileMode.Open, FileAccess.Read);
                var fileReader = new BinaryReader(fileStream);

                fileStream.Seek(0x14, SeekOrigin.Begin);     // Seek to flags
                uint flags = fileReader.ReadUInt32();        // Read flags

                if ((flags & 1U) == 1U)
                {
                    // Bit 1 set means we have to skip the shell item ID list
                    fileStream.Seek(0x4c, SeekOrigin.Begin); // Seek to the end of the header
                    ushort offset = fileReader.ReadUInt16();   // Read the length of the Shell item ID list
                    fileStream.Seek(offset, SeekOrigin.Current); // Seek past it (to the file locator info)
                }

                long fileInfoStartsAt = fileStream.Position; // Store the offset where the file info structure begins
                uint totalStructLength = fileReader.ReadUInt32(); // read the length of the whole struct
                fileStream.Seek(0xc, SeekOrigin.Current); // seek to offset to base pathname
                uint fileOffset = fileReader.ReadUInt32(); // read offset to base pathname
                                                           // the offset is from the beginning of the file info struct (fileInfoStartsAt)
                fileStream.Seek(fileInfoStartsAt + fileOffset, SeekOrigin.Begin); // Seek to beginning of
                                                                                    // base pathname (target)
                long pathLength = (totalStructLength + fileInfoStartsAt) - fileStream.Position - 1; // read the base pathname. I don't need the 2 terminating nulls.
                char[] linkTarget = fileReader.ReadChars((int)pathLength); // should be unicode safe
                var link = new string(linkTarget);
                int begin = link.IndexOf("\0\0");

                if (link[link.Length - 1] == '\0')
                    link = link.Substring(0, link.Length - 1);

                if (begin > -1)
                {
                    int end = link.IndexOf("\\\\", begin + 2) + 2;
                    end = link.IndexOf('\0', end) + 1;

                    string firstPart = link.Substring(0, begin);
                    string secondPart = link.Substring(end);

                    return firstPart + secondPart;
                }
                else
                {
                    return link;
                }
            }
            catch//(Exception ex)
            {
                // var ss = ex.Message;
                return string.Empty;
            }
            finally
            {
                if (fileStream != null)
                    fileStream.Dispose();
            }
        }

        private void GetInfoFromRegistry(
            RegistryKey rk, string pathToKey,
            Action<Info> addItemAction, Action<Delegate, object[]> invokeInUIThread)
        {
            var sk = rk.OpenSubKey(
                pathToKey, RegistryKeyPermissionCheck.ReadSubTree, RegistryRights.ReadKey);

            if (sk == null) return;

            string filePath;
            Info info;
            foreach (var it in sk.GetValueNames())
            {
                info = new Info("Registry");
                filePath = SetPathInfo((string)sk.GetValue(it), info);
                //если файл системный, то File.Exists может вернуть false
                // https://docs.microsoft.com/en-us/dotnet/api/system.io.file.getaccesscontrol?view=netframework-4.7.2
                if (File.Exists(filePath))
                {
                    SetSignatureInfo(filePath, info);

                    buffer2_[0] = info;
                    buffer2_[1] = filePath;
                    invokeInUIThread(extractIconAndAddItem_, buffer2_);
                }
                else
                {
                    buffer1_[0] = info;
                    invokeInUIThread(addItemAction, buffer1_);
                }
            }

            if (sk.SubKeyCount > 0)
            {
                var names = sk.GetSubKeyNames();

                for (int i = 0; i < names.Length; ++i)
                {
                    GetInfoFromRegistry(sk, names[i], addItemAction, invokeInUIThread);
                }
            }

            sk.Dispose();
        }

        /// <summary>
        /// Заполняет информацию о путях и аргументах коммандной строки для файла
        /// </summary>
        /// <param name="rawPath">Путь к файлу</param>
        /// <param name="info">Информация о файле</param>
        /// <returns>Абсолютный путь к файлу, если удалось преобразовать.
        /// В противном случае тот же самый путь.</returns>
        private string SetPathInfo(string rawPath, Info info)
        {
            string path;

            if (rawPath.Length > 0 && rawPath[0] == '\"')
            {
                int endIndex = rawPath.IndexOf('\"', 1);
                path = NormalizePath(rawPath.Substring(1, endIndex - 1));

                if (endIndex + 1 < rawPath.Length)
                    info.CmdArguments = rawPath.Substring(endIndex + 2);
            }
            else
            {
                if (rawPath.StartsWith(rightProgramFiles))
                {
                    path = NormalizePath(rawPath);
                }
                else
                {
                    int endIndex = rawPath.IndexOf(' ');

                    if (endIndex > -1)
                    {
                        path = NormalizePath(rawPath.Substring(0, endIndex));
                        info.CmdArguments = rawPath.Substring(endIndex + 1);
                    }
                    else
                    {
                        path = NormalizePath(rawPath);
                    }
                }
            }

            if (path.Length > 0)
            {
                info.FileName = Path.GetFileName(path);
                info.Directory = Path.GetDirectoryName(path);
            }
            else
            {
                info.FileName = string.Empty;
                info.Directory = info.FileName;
            }

            return path;
        }

        /// <summary>
        /// Заполняет информацию о цифровой подписи файла
        /// 1 есть ли подпись
        /// 2 корректна ли подпись
        /// 3 разработчик (файла)
        /// </summary>
        /// <param name="path">Путь к файлу</param>
        /// <param name="data">Информация о файле для заполнения</param>
        private void SetSignatureInfo(string path, Info data)
        {
            Pipeline pipeline = runspace_.CreatePipeline();
            pipeline.Commands.AddScript("Get-AuthenticodeSignature \"" + path + "\"");

            Collection<PSObject> results = pipeline.Invoke();
            var signature = (Signature)results[0].BaseObject;

            if (signature != null)
            {
                var s = signature.Status;
                data.IsSignatureExists =
                    s != SignatureStatus.UnknownError &&
                    s != SignatureStatus.NotSupportedFileFormat &&
                    s != SignatureStatus.NotSigned;

                data.IsSignatureValid = s == SignatureStatus.Valid;

                var signerCertificate = signature.SignerCertificate;

                if (signerCertificate != null)
                {
                    var issuer = signerCertificate.IssuerName;
                    var properties = issuer.Format(true);
                    int index = properties.IndexOf("O=");
                    int endIndex = properties.IndexOf("\r\n", index += 2);

                    data.Manufacturer = properties.Substring(index, endIndex - index);
                }
            }

            pipeline.Dispose();
        }

        /// <summary>
        /// Извлекает иконку файла по его имени.
        /// </summary>
        /// <param name="fileName">Имя файла</param>
        /// <returns>Иконка файла</returns>
        private ImageSource ExtractIcon(string fileName)
        {
            if (iconsCache_.TryGetValue(fileName, out ImageSource value))
                return value;
            else
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(fileName);

                value = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            new System.Windows.Int32Rect(0, 0, icon.Width, icon.Height),
                            emptyOptions);

                iconsCache_.Add(fileName, value);

                icon.Dispose();

                return value;
            }
        }

        /// <summary>
        /// Делает из относительного пути к файлу абсолютный
        /// </summary>
        /// <param name="path">Путь, что может быть относительным</param>
        /// <returns>Абсолютный путь к файлу, если удалось преобразовать.
        /// В противном случае тот же самый путь.</returns>
        private string NormalizePath(string path)
        {
            if (path.StartsWith(windir))
                return path.Replace(windir, rightWinDir);
            else
            if (path.StartsWith(sys32, StringComparison.InvariantCultureIgnoreCase))
                return rightSystemRoot + path;
            else
            if (path.StartsWith(sysRoot, StringComparison.InvariantCultureIgnoreCase))
                return path.Replace(sysRoot, rightSystemRoot);
            else
            if (path.StartsWith(sysRoot2, StringComparison.InvariantCultureIgnoreCase))
                return path.Replace(sysRoot2, rightSystemRoot);
            else
            if (path.StartsWith(localAppData))
                return path.Replace(localAppData, rightLocalAppData);
            else
            if (path.StartsWith(programFiles, StringComparison.InvariantCultureIgnoreCase))
                return path.Replace(programFiles, rightProgramFiles);
            else
                return path;
        }

        #endregion private methods
    }
}