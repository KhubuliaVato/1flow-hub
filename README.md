# 1Flow HUB

პროგრამების ცენტრი: კატალოგი GitHub-იდან, ინსტალაცია / გაშვება / განახლება, განახლებების ისტორია,
tray და HUB-ის თვითგანახლება. WPF (.NET 10), ფრიად მსუბუქი, გარე ბიბლიოთეკების გარეშე.

## გაშვება და ბილდი
```powershell
dotnet run -c Release                 # დეველოპმენტი
powershell -ExecutionPolicy Bypass -File build.ps1   # → 1FlowHub.zip (self-contained, single exe)
```

## როგორ მუშაობს
* **კატალოგი** — `Settings.CatalogUrl` (ნაგულისხმევად `tvflow-hub/main/catalog.json`).
  ახალი პროგრამა = ახალი ჩანაწერი `catalog.json`-ში (`repoOwner`, `repoName`, `assetMatch`, `executableName`), HUB-ის განახლება არ სჭირდება.
* **ინსტალაცია** — GitHub Releases-ის ბოლო ვერსია → `%LOCALAPPDATA%\1Flow\Apps\<id>`.
* **HUB-ის განახლება** — ეძებს release-ს `HubOwner/HubRepo`-ში, რომელსაც აქვს ფაილი `1FlowHub.zip`
  და ვერსია მეტია მიმდინარეზე (`<Version>` `1FlowHub.csproj`-ში).
* **ლოგი** — `%LOCALAPPDATA%\1Flow\hub.log`.

## გამოშვება
1. გაზარდე `<Version>` `1FlowHub.csproj`-ში
2. `build.ps1`
3. GitHub-ზე შექმენი release (tag `v2.x.x`) და ატვირთე `1FlowHub.zip`
