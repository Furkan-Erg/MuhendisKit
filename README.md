# MühendisKit

Makine mühendisi ve kontrol mühendisi için Windows uygulaması. Tek pencerede şunları içerir:

| Modül | Ne yapar | Çekirdek |
|---|---|---|
| **Mekanik hesaplar** | Oda bazında ısı kaybı (TS 2164 yapısı), boru çapı (Darcy–Weisbach + Colebrook), kanal çapı (eşit sürtünme, Huebscher), psikrometri (ASHRAE), kapalı genleşme tankı (TS EN 12828), doğalgaz boru çapı (Renouard), birim çevirici | `MuhendisKit.Core` (C#, birim testli) |
| **Isı yalıtımı** | Katman katman U değeri, TS 825:2024 sınır kontrolü, gerekli yalıtım kalınlığı, Glaser yoğuşma analizi (TS EN ISO 13788), PDF rapor | `MuhendisKit.Core` |
| **Hakediş** | Poz kataloğu (ÇŞİDB Excel içe aktarma), metraj / yeşil defter, hakediş icmali, fiyat farkı F = An × B × (Pn − 1), TCMB EVDS endeksleri, Excel ve PDF çıktısı | `MuhendisKit.Core` + ClosedXML |
| **Çizim (2B)** | DWG/DXF açma, çizme, düzenleme, ölçülendirme; çizimden metraj (katman uzunlukları, kapalı alanlar, blok sayıları) ve hakedişe aktarma | [MLightCAD](https://github.com/mlightcad/cad-viewer) (MIT) + ACadSharp |
| **Katı model (3B)** | Taslak, ekstrüzyon, döndürme, süpürme, loft, boolean, radyüs/pah; STEP/IGES/BREP/STL | [Chili3D](https://github.com/xiangechen/chili3d) (AGPL-3.0) + OpenCascade |

**Claude ile kullanım:** `MuhendisKit.Mcp.exe` bir MCP sunucusudur. Claude Code veya Claude Desktop ona bağlanınca açık MühendisKit penceresini kullanabilir: hesap yapar, 2B çizer (script API), 3B modeller, projeye hakediş satırı girer, ekran görüntüsüyle sonucu kontrol eder. Bağlantı komutu uygulamada **Ayarlar → Claude ile kullanım** kısmındadır:

```
claude mcp add --scope user muhendiskit -- "C:\Program Files\MuhendisKit\MuhendisKit.Mcp.exe"
```

## Standart değerleri hakkında

TS 825:2024 sınır U değerleri, TS 2164 dış hesap sıcaklıkları ve artırımlar TSE'nin ücretli standartlarındadır. Program bunları **tahmin etmez**: değerler mühendis tarafından **Ayarlar** sayfasından ya da projede girilir. Malzeme kütüphanesindeki λ değerleri tipik değerlerdir. Projede üreticinin beyan ettiği değer kullanılmalıdır.

## Derleme

Gerekenler: .NET 10 SDK, Node.js 24, Git.

```powershell
pwsh tools/build-web.ps1          # 2B (MLightCAD) ve 3B (Chili3D + overlay) modüllerini wwwroot'a derler
dotnet test                       # hesap motoru testleri
dotnet build src/MuhendisKit      # uygulama + MCP sunucusu
```

- `tools/cad2d`: MLightCAD editörü ve köprü (`src/bridge.ts`).
- `tools/cad3d`: Chili3D sabitlenmiş bir commit'ten derlenir. `overlay/` altındaki dosyalar upstream ağacın üzerine kopyalanır. Bu dosyalar Türkçe dil dosyası, MühendisKit köprüsü (`packages/web/src/mkBridge.ts`) ve analitik etiketi çıkarılmış `index.html`'dir.

## Lisans

MühendisKit, GNU Affero Genel Kamu Lisansı 3.0 (AGPL-3.0) ile dağıtılır (bkz. `LICENSE`). Kullanılan bileşenler:

- MLightCAD (MIT)
- LibreDWG / libredwg-web (GPL-3.0)
- Chili3D (AGPL-3.0)
- Open CASCADE Technology (LGPL-2.1)
- ACadSharp (MIT)
- ClosedXML (MIT)
- three.js (MIT)
- Microsoft WebView2 SDK
