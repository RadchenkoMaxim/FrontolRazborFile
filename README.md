# Анализатор файла обмена Frontol

Windows-приложение на C# и WPF для построчного разбора файлов загрузки Frontol 6.

Текущая версия: **1.1.1**. Разработчик: [Maximum IT](https://maximumit25.ru/nfr).

## Что умеет

- автоматически определяет UTF-8 или Windows-1251;
- показывает все физические строки файла: заголовок, команды, данные и комментарии;
- разделяет строки данных по `;` с сохранением пустых и завершающих сегментов;
- раскрывает товарные команды в таблицу из 68 полей;
- показывает исходное значение, расшифровку, тип, обязательность и назначение поля;
- расшифровывает флаги, тип номенклатуры/маркировки, единицу измерения и другие перечисления;
- проверяет обязательные значения, типы, длину строк и лишние поля;
- поддерживает поиск, фильтр ошибок и перетаскивание файла в окно;
- показывает индикатор обработки больших файлов;
- позволяет скрывать пустые или выбранные пользователем поля и сохраняет настройки отдельно для каждой команды;
- содержит встроенный справочник всех 38 кодов типа номенклатуры/маркировки из поля 55;
- открывает текущий исходный файл системной программой или показывает его в Проводнике;
- позволяет скрыть список строк кнопкой на разделителе или сочетанием `Ctrl+M`;
- показывает подробную многострочную расшифровку только там, где она действительно нужна.

Встроенный справочник содержит команды:

- `$$$ADDQUANTITY`;
- `$$$REPLACEQUANTITY`;
- `$$$REPLACEQUANTITYWITHOUTSALE`;
- `$$$DELETEBARCODESBYWARECODE`;
- `$$$ADDCLASSIFIERLINKS`.

Для остальных команд приложение все равно разделяет строку на поля и показывает их номера, но сообщает, что подробного описания команды во встроенном справочнике пока нет.

## Запуск

Готовая версия находится в `artifacts/FrontolFileAnalyzer-1.1.1-win-x64-self-contained`. Запустите единственный файл `FrontolFileAnalyzer.exe`, нажмите «Выбрать файл...» или перетащите файл обмена в окно.

Сборка self-contained: .NET 10 Desktop Runtime x64 уже включен внутрь EXE. Устанавливать .NET отдельно не требуется.

## Сборка из исходников

```powershell
dotnet restore .\FrontolFileAnalyzer.slnx --configfile .\NuGet.Config
dotnet build .\FrontolFileAnalyzer.slnx -c Release --no-restore
dotnet restore .\src\FrontolFileAnalyzer\FrontolFileAnalyzer.csproj -r win-x64 --configfile .\NuGet.SelfContained.Config
dotnet publish .\src\FrontolFileAnalyzer\FrontolFileAnalyzer.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o .\artifacts\FrontolFileAnalyzer-1.1.1-win-x64-self-contained
```

Проверка на приложенном примере:

```powershell
dotnet run --no-restore --project .\tests\FrontolFileAnalyzer.SmokeTests\FrontolFileAnalyzer.SmokeTests.csproj -- .\tests\fixtures\base-sample.txt
```

## Источник формата

Справочник полей составлен по «Руководству интегратора» Frontol 6:

- товарные команды - раздел 17.2.1.1, страницы 190-200;
- коды типа номенклатуры/маркировки - поле 55, страницы 195-198;
- удаление штрихкодов товара - раздел 17.2.1.4, страница 200;
- связь с классификатором - раздел 17.2.1.48, страница 253.
