using System;
using System.Linq;
using TFlex.DOCs.Model.Macros;
using TFlex.DOCs.Model.Macros.ObjectModel;
using TFlex.DOCs.Model.References.Documents;
using TFlex.DOCs.Model.References.Files;

namespace UserFolders
{
    public class Macro : MacroProvider
    {
        private const string Separator = "^";

        public Macro(MacroContext context)
            : base(context)
        {
        }

        public override void Run()
        {
        }

        public string GetDefaultFolder() => ПолучитьПапкуДляХраненияФайловДокумента();

        public string ПолучитьПапкуДляХраненияФайловДокумента()
        {
            if (ТекущийОбъект == null)
                return string.Empty;

            ClassObjectAccessor типОбъекта = ТекущийОбъект.Тип;

            if (типОбъекта.ПорожденОт("7c41c277-41f1-44d9-bf0e-056d930cbb14"))
                return @"Файлы документов\Конструкторско-технологические документы\3D модели деталей";

            if (типОбъекта.ПорожденОт("dd2cb8e8-48fa-4241-8cab-aac3d83034a7"))
                return @"Файлы документов\Конструкторско-технологические документы\Сборочные 3D модели";

            if (типОбъекта.ПорожденОт("023ab6db-b1aa-491f-a106-d791322ea764") ||
                типОбъекта.ПорожденОт("d1510810-ba4f-4a34-96a6-58d4a6e39674") ||
                типОбъекта.ПорожденОт("11d2fb6f-baa7-401c-bbd9-7f3222f5c5e8"))
                return @"Файлы документов\Конструкторско-технологические документы\Изделия, комплекты и комплексы";

            if (типОбъекта.ПорожденОт("582dad76-1b07-4c4b-b97d-cc89b0149aa6"))
                return @"Файлы документов\Конструкторско-технологические документы\Стандартные изделия";

            if (типОбъекта.ПорожденОт("83e1ef55-0658-4e3e-afeb-d8fceee3c86d"))
                return @"Файлы документов\Конструкторско-технологические документы\Прочие изделия";

            if (типОбъекта.ПорожденОт("d6324424-a39e-4112-a207-7e96a1971852"))
                return @"Файлы документов\Конструкторско-технологические документы\Сборочные чертежи";

            if (типОбъекта.ПорожденОт("b06b4ee9-36ce-441c-856b-12a6a11311ea"))
                return @"Файлы документов\Конструкторско-технологические документы\Аннотации";

            if (типОбъекта.ПорожденОт("8db6f280-66b8-4ed2-a82b-2ad6e8d27175"))
                return @"Файлы документов\Конструкторско-технологические документы\Извещения об изменениях";

            if (типОбъекта.ПорожденОт("d4caa669-e807-42cb-8679-09f933d8683e"))
                return @"Файлы документов\Конструкторско-технологические документы\Чертежи деталей";

            if (типОбъекта.ПорожденОт("13b7e4f2-c0e4-481a-a55d-e6c81157b4f4"))
                return @"Файлы документов\Конструкторско-технологические документы\Ведомости";

            if (типОбъекта.ПорожденОт("8ad12d22-0dfc-4090-a2d5-894b86ed565a"))
                return @"Файлы документов\Конструкторско-технологические документы\Отчёты";

            if (типОбъекта.ПорожденОт("9745b167-0e66-43c2-91c0-899a0149b19e"))
                return @"Файлы документов\Конструкторско-технологические документы\Технологические документы";

            if (типОбъекта.ПорожденОт("f4a65a70-580a-4404-a7f9-c24b2c7dd2bd"))
                return @"Файлы документов\Конструкторско-технологические документы\Текстовые документы";

            if (типОбъекта.ПорожденОт("cff289ad-fa9b-44d8-864e-aeebca63b952"))
                return @"Файлы документов\Конструкторско-технологические документы\Программы для станков с ЧПУ";

            return @"Файлы документов\Конструкторско-технологические документы";
        }

        public string GetDefaultFileName()
        {
            string defaultFileName = string.Empty;

            var document = Context.ReferenceObject as EngineeringDocumentObject;
            if (document is null)
                return defaultFileName;

            string denotation = document.Denotation;
            string name = document.Name;

            if (document.Reference.ParameterGroup.SupportsRevisions)
            {
                string revisionName = document.SystemFields.RevisionName;

                if (string.IsNullOrEmpty(denotation))
                {
                    if (string.IsNullOrEmpty(name))
                        return string.Empty;

                    defaultFileName = $"{name}{Separator}{revisionName}";
                }
                else
                {
                    defaultFileName = $"{denotation}{Separator}{revisionName}";
                }
            }
            else
            {
                defaultFileName = string.IsNullOrEmpty(denotation)
                    ? name
                    : $"{denotation}{Separator}{name}";
            }

            char[] invalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();
            defaultFileName = invalidFileNameChars.Aggregate(defaultFileName, (acc, c) => acc.Replace(c, '_'));

            return defaultFileName;
        }

        // Entry point used by backend adapter: ensures a folder path and returns resolved relative path.
        public string EnsureFolderPath(string folderPath)
        {
            var normalizedPath = NormalizePath(folderPath);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                return string.Empty;

            var fileReference = new FileReference(Context.Connection);
            if (fileReference.FindByRelativePath(normalizedPath) is FolderObject existingFolder)
                return NormalizePath(existingFolder.Path.Value);

            var segments = normalizedPath
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToArray();

            if (segments.Length == 0)
                return string.Empty;

            FolderObject parent = null;
            var existingDepth = 0;

            for (var depth = segments.Length; depth > 0; depth--)
            {
                var probePath = string.Join("/", segments.Take(depth));
                if (fileReference.FindByRelativePath(probePath) is FolderObject probeFolder)
                {
                    parent = probeFolder;
                    existingDepth = depth;
                    break;
                }
            }

            if (parent == null)
                throw new InvalidOperationException("Cannot resolve existing prefix for path: " + normalizedPath);

            for (var i = existingDepth; i < segments.Length; i++)
            {
                parent = fileReference.CreatePath(segments[i], parent, CreateImportParameters());
            }

            return parent == null ? normalizedPath : NormalizePath(parent.Path.Value);
        }

        private static ImportParameters CreateImportParameters()
        {
            return new ImportParameters
            {
                Recursive = false,
                CreateClasses = true,
                AutoCheckIn = true,
                UpdateExistingFiles = false
            };
        }

        private static string NormalizePath(string path)
        {
            return (path ?? string.Empty)
                .Replace('\\', '/')
                .Trim()
                .Trim('/');
        }
    }
}
