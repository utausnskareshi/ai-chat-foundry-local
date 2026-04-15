# AI Chat - Foundry Local

Windows 11 上でローカル AI モデルとチャットできる WPF デスクトップアプリケーションです。  
**Microsoft Foundry Local SDK** を使用し、インターネット接続なしで AI との会話を実現します。

---

## 特徴

- 🤖 **完全ローカル動作** — クラウド API 不要。すべての推論がローカルで完結
- 🛡️ **プロセス分離アーキテクチャ** — 推論エンジンを別プロセスで実行し、ネイティブクラッシュからメインアプリを保護
- 🎨 **ダークテーマ UI** — Windows 11 デザインガイドラインに沿ったモダンな見た目
- 📦 **モデル管理** — ダウンロード・ロード・アンロード・削除をアプリ内から操作可能
- ⚡ **GPU/CPU 対応** — OpenVINO を使った GPU 推論と CPU 推論に対応

---

## 動作要件

| 項目 | 要件 |
|------|------|
| OS | Windows 11 (ビルド 26100 以上) |
| .NET | .NET 10 SDK |
| アーキテクチャ | x64 |
| Foundry Local | Microsoft AI Foundry Local SDK 1.0.0 |
| Visual Studio | Visual Studio 2022 / 2026 以降 (Windows App SDK ワークロード) |

> **注意:** Windows Smart App Control が「評価モード」または「オン」になっている場合、  
> ビルドした未署名の DLL がブロックされることがあります。開発時は Smart App Control を  
> 「**オフ**」に設定してください。  
> （設定 → プライバシーとセキュリティ → Windows セキュリティ → アプリとブラウザーの制御 → Smart App Control の設定）

---

## セットアップ

### 1. リポジトリのクローン

```bash
git clone https://github.com/<your-username>/AIChatFoundryLocal.git
cd AIChatFoundryLocal
```

### 2. NuGet パッケージの復元

Visual Studio でソリューションを開くと自動的に復元されます。  
コマンドラインの場合:

```bash
dotnet restore
```

### 3. ビルド

```bash
dotnet build
```

または Visual Studio で **「ビルド」→「ソリューションのリビルド」** を実行してください。

### 4. 実行

Visual Studio で **F5** を押すか、以下のコマンドで実行します:

```bash
dotnet run --project src/AIChatFoundryLocal/AIChatFoundryLocal.csproj
```

---

## 使い方

### モデルのセットアップ

1. 左ナビゲーションの **「モデル管理」** をクリック
2. 使用したいモデルの **「ダウンロード」** ボタンをクリック（初回のみ）
3. ダウンロード完了後、**「ロード」** ボタンをクリック
4. ステータスが「ロード済み」になったら準備完了

### チャット

1. 左ナビゲーションの **「チャット」** をクリック
2. 下部のテキストボックスにメッセージを入力
3. **「送信」** ボタンまたは **Enter** キーで送信
4. AI の応答が画面に表示されます

### モデルの選択ガイド

| モデル | サイズ | 特徴 | 推奨用途 |
|--------|--------|------|---------|
| qwen2.5-0.5b | 366 MB | 超軽量・高速 | 動作確認・軽い質問 |
| qwen2.5-1.5b | 1019 MB | バランス型 | 日常的な質問 |
| phi-4-mini | 2205 MB | Microsoft 製・多機能 | 汎用チャット |
| deepseek-r1-1.5b | 1352 MB | 推論特化 | 論理的な問題解決 |
| qwen2.5-coder-0.5b | 365 MB | コード生成特化 | プログラミング支援 |

> GPU モデルは GPU 上で動作します。VRAM が不足する場合は小さいモデルを選択してください。

---

## プロジェクト構成

```
AIChatFoundryLocal/
├── src/
│   ├── AIChatFoundryLocal/                  # メイン WPF アプリ
│   │   ├── App.xaml / App.xaml.cs           # アプリケーション定義・例外ハンドラー
│   │   ├── MainWindow.xaml / .cs            # メインウィンドウ・ナビゲーション
│   │   ├── Converters/                      # WPF バリューコンバーター
│   │   │   ├── BoolToVisibilityConverter.cs # bool → Visibility 変換
│   │   │   └── ChatRoleConverters.cs        # ChatRole → 色・配置・ラベル変換
│   │   ├── Helpers/
│   │   │   └── ServiceCrashHelper.cs        # クラッシュ検出・再起動ダイアログ
│   │   ├── Models/
│   │   │   ├── ChatMessage.cs               # チャットメッセージモデル
│   │   │   └── LocalModelInfo.cs            # ローカルモデル情報モデル
│   │   ├── Services/
│   │   │   └── FoundryLocalService.cs       # ServiceHost との通信サービス
│   │   ├── ViewModels/
│   │   │   ├── ViewModelBase.cs             # ViewModel 基底クラス
│   │   │   ├── MainViewModel.cs             # メイン ViewModel（初期化・画面切替）
│   │   │   ├── ChatViewModel.cs             # チャット画面 ViewModel
│   │   │   └── ModelManagementViewModel.cs  # モデル管理画面 ViewModel
│   │   └── Views/
│   │       ├── ChatView.xaml / .cs          # チャット画面 View
│   │       └── ModelManagementView.xaml / .cs # モデル管理画面 View
│   │
│   └── AIChatFoundryLocal.ServiceHost/      # 推論プロセス（別プロセス）
│       └── Program.cs                       # ServiceHost エントリポイント
│
├── Directory.Build.targets                  # Foundry Local ネイティブ DLL のコピー設定
└── AIChatFoundryLocal.sln                   # ソリューションファイル
```

---

## アーキテクチャ

### プロセス分離設計

```
┌─────────────────────────────────┐       ┌──────────────────────────────────┐
│  AIChatFoundryLocal (WPF)       │       │  AIChatFoundryLocal.ServiceHost  │
│                                 │       │                                  │
│  ┌─────────────────────────┐   │       │  ┌────────────────────────────┐  │
│  │  FoundryLocalService    │   │       │  │  Foundry Local SDK         │  │
│  │                         │   │ stdin │  │  (FoundryLocalManager)     │  │
│  │  カタログ操作            │──────────────▶│                            │  │
│  │  (LIST/DOWNLOAD/LOAD等) │◀──────────────│  OpenAI 互換 Web API       │  │
│  │                         │  stdout  │  │  POST /v1/chat/completions  │  │
│  │  チャット推論            │──────────────▶│                            │  │
│  │  (HTTP POST)            │◀──────────────│  推論エンジン               │  │
│  └─────────────────────────┘   │  HTTP │  │  (OpenVINO / WinML)        │  │
│                                 │       │  └────────────────────────────┘  │
└─────────────────────────────────┘       └──────────────────────────────────┘
```

**分離の理由:**  
Foundry Local SDK の推論エンジンはネイティブコードで実装されており、  
推論中に `0xC0000409`（Stack Buffer Overrun）等のネイティブクラッシュが発生することがあります。  
.NET の例外機構ではネイティブクラッシュを捕捉できないため、推論処理を別プロセスに分離することで  
**メインアプリが落ちない**設計にしています。

### 通信プロトコル

**stdin/stdout（カタログ操作）:**

| コマンド | 引数 | 説明 |
|---------|------|------|
| `LIST_MODELS` | なし | チャット対応モデルの一覧を JSON で返す |
| `DOWNLOAD_MODEL\|<alias>` | モデル名 | 指定モデルをダウンロード（進捗を `PROGRESS\|nn.n` で通知） |
| `LOAD_MODEL\|<alias>` | モデル名 | 指定モデルをメモリにロード |
| `UNLOAD_MODEL\|<alias>` | モデル名 | 指定モデルをアンロード |
| `DELETE_MODEL\|<alias>` | モデル名 | キャッシュを削除 |
| `QUIT` | なし | ServiceHost を正常終了 |

**HTTP（チャット推論）:**

```
POST http://127.0.0.1:<port>/v1/chat/completions
Content-Type: application/json

{
  "model": "qwen2.5-0.5b",
  "messages": [
    { "role": "system", "content": "You are a helpful assistant." },
    { "role": "user", "content": "こんにちは" }
  ],
  "stream": false
}
```

---

## ログファイル

アプリの動作ログは以下のパスに保存されます:

| ファイル | 内容 |
|---------|------|
| `%LOCALAPPDATA%\AIChatFoundryLocal\service.log` | ServiceHost との通信ログ・HTTP 通信ログ |
| `%LOCALAPPDATA%\AIChatFoundryLocal\crash.log` | 未処理例外・クラッシュログ |

問題が発生した場合はこれらのログを確認してください。

---

## トラブルシューティング

### アプリが起動しない / 初期化エラーが表示される

- ソリューション全体をリビルドしてください（`AIChatFoundryLocal.ServiceHost.exe` が必要）
- `obj` / `bin` フォルダを削除してからリビルドしてください
- Visual Studio を管理者権限で実行してみてください

### チャットで HTTP 500 エラーが発生する

- **音声認識モデル（Whisper）はチャットに対応していません**。qwen・phi・deepseek 等のモデルを使用してください
- モデルのロードが完了しているか確認してください

### 推論サービスが停止したダイアログが表示される

- 推論エンジン内部でネイティブクラッシュが発生しました
- **別のモデルを試してください**（特に GPU モデルは VRAM 不足でクラッシュすることがあります）
- アプリを再起動してください

### ビルドエラー: "別のプロセスで使用されているためアクセスできません"

1. アプリを終了してください
2. タスクマネージャーで `AIChatFoundryLocal.ServiceHost.exe` が残っていれば終了してください
3. Visual Studio で **「ビルド」→「ソリューションのクリーン」** を実行してください
4. 再度リビルドしてください

---

## 使用技術・ライブラリ

| 技術 | バージョン | 用途 |
|------|-----------|------|
| .NET | 10.0 | ランタイム |
| WPF | .NET 10 | UI フレームワーク |
| Microsoft AI Foundry Local SDK | 1.0.0 | ローカル AI 推論 |
| CommunityToolkit.Mvvm | 8.4.2 | MVVM パターン実装 |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.142 | XAML ビヘイビアー |
| Windows App SDK | 1.8 | Windows ネイティブ機能 |

---

## ライセンス

このプロジェクトは [MIT License](LICENSE) のもとで公開されています。

---

## 注意事項

- このアプリは Microsoft の公式製品ではありません
- Foundry Local SDK の使用には Microsoft の利用規約が適用されます
- AI モデルの出力は必ずしも正確ではありません。重要な判断には使用しないでください
