# Raccoon.DnsRelay

クライアントからの DNS クエリ（UDP）を上流 DNS サーバーへ転送（リレー）するサービスです。
Windows サービス / systemd デーモンとして常駐し、TTL キャッシュと OpenTelemetry による可観測性を備えます。

- .NET 10 / Worker サービス
- UDP リレー + 上流フェイルオーバー
- 低アロケーション設計（`Span` / `ArrayPool` / 生 `Socket`）
- 効率的な TTL キャッシュ（割り当てゼロ参照・TTL 減算・GC 配慮）
- OpenTelemetry（メトリクス / トレース、設定で有効化）

設計の詳細は [`docs/design.md`](docs/design.md) を参照してください。

## 必要環境

- .NET 10 SDK / ランタイム

## ビルド

```pwsh
dotnet build Raccoon.DnsRelay/Raccoon.DnsRelay.csproj -c Release
```

## 実行

```pwsh
# 既定設定（appsettings.json）で起動
dotnet run --project Raccoon.DnsRelay

# 開発時（appsettings.Development.json: 127.0.0.1:15353）
$env:DOTNET_ENVIRONMENT = "Development"
dotnet run --project Raccoon.DnsRelay
```

> **ポート 53 は要管理者権限**（Linux は要 root / `CAP_NET_BIND_SERVICE`）。
> Windows の既定 mDNS がポート 5353 を占有するため、開発既定は **15353** にしています。

動作確認（別ターミナル）:

```pwsh
nslookup -port=15353 example.com 127.0.0.1
```

## 設定（appsettings.json）

| セクション | キー | 既定 | 説明 |
| --- | --- | --- | --- |
| `Server` | `Address` / `Port` | `0.0.0.0` / `53` | リッスンアドレス / ポート |
| | `ReceiveBufferSize` | `4096` | 受信バッファ（EDNS0 考慮） |
| | `MaxConcurrentQueries` | `1024` | 同時処理上限（超過分はドロップ） |
| `Upstream` | `Servers` | `["8.8.8.8","1.1.1.1"]` | 上流 DNS（先頭から順にフェイルオーバー） |
| | `Port` / `TimeoutMs` | `53` / `3000` | 上流ポート / タイムアウト |
| `Cache` | `Enabled` | `true` | キャッシュ有効化 |
| | `MaxEntries` | `10000` | 最大エントリ数 |
| | `MinTtlSeconds` / `MaxTtlSeconds` | `5` / `86400` | TTL クランプ範囲 |
| | `NegativeTtlSeconds` | `0` | 負キャッシュ TTL（0 で無効） |
| | `DecrementTtl` | `true` | ヒット時に残り秒へ TTL を書換 |
| | `CleanupIntervalSeconds` | `60` | 期限切れ掃除の間隔 |
| `Telemetry` | `Enabled` | `false` | OpenTelemetry 有効化 |
| | `ServiceName` | `Raccoon.DnsRelay` | リソースのサービス名 |
| | `EnableMetrics` / `EnableTracing` | `true` / `true` | メトリクス / トレース |
| | `EnableRuntimeInstrumentation` | `false` | ランタイムメトリクス |
| | `Otlp.Endpoint` / `Otlp.Protocol` | `""` / `Grpc` | OTLP push 先（空=無効。`Grpc` / `HttpProtobuf`） |
| | `Prometheus.Enabled` | `false` | Prometheus pull エンドポイントを公開 |
| | `Prometheus.Host` / `Prometheus.Port` | `localhost` / `9464` | scrape ホスト / ポート |
| | `Prometheus.ScrapeEndpointPath` | `/metrics` | scrape パス |
| | `EnableConsoleExporter` | `false` | コンソール出力 |

設定は環境変数やコマンドライン引数でも上書きできます（例: `--Server:Port=15353`、`--Telemetry:Enabled=true`）。
ログ出力は `Serilog` セクション（Console / File）で設定します。

## 可観測性（OpenTelemetry）

`Telemetry:Enabled=true` で有効化されます（マスタスイッチ）。無効時は計装コードが no-op となりオーバーヘッドはありません。

- メトリクス（Meter `Raccoon.DnsRelay`）: `dns.relay.queries` / `cache.hits` / `cache.misses` / `upstream.requests` / `upstream.failures` / `dropped` / `active` / `duration`
- トレース（ActivitySource `Raccoon.DnsRelay`）: `dns.relay.query`（親） / `dns.relay.upstream`（子）

エクスポータは独立に選択できます:

- **OTLP push**: `Otlp.Endpoint` を設定（空なら無効）。
- **Prometheus pull**: `Prometheus.Enabled=true`。`http://{Host}:{Port}{ScrapeEndpointPath}`（既定 `http://localhost:9464/metrics`）を公開し、Prometheus 側から scrape します（OTLP 送信なしの構成も可）。
- **Console**: `EnableConsoleExporter=true`。

```pwsh
# OTLP push（コレクタへ送信）
dotnet run --project Raccoon.DnsRelay -- --Telemetry:Enabled=true --Telemetry:Otlp:Endpoint=http://localhost:4317

# Prometheus pull のみ（OTLP 送信なし）
dotnet run --project Raccoon.DnsRelay -- --Telemetry:Enabled=true --Telemetry:Prometheus:Enabled=true
```

Prometheus の scrape 設定例:

```yaml
scrape_configs:
  - job_name: raccoon-dnsrelay
    static_configs:
      - targets: ['localhost:9464']
```

## Windows サービス

```pwsh
# 発行
dotnet publish Raccoon.DnsRelay/Raccoon.DnsRelay.csproj -c Release -r win-x64 --self-contained false -o publish

# 登録（管理者権限。binPath= の後の空白は必須）
sc.exe create Raccoon.DnsRelay binPath= "C:\path\to\publish\Raccoon.DnsRelay.exe" start= auto
sc.exe start Raccoon.DnsRelay

# 解除
sc.exe stop Raccoon.DnsRelay
sc.exe delete Raccoon.DnsRelay
```

## systemd（Linux）

```ini
[Unit]
Description=Raccoon DNS Relay
After=network.target

[Service]
Type=notify
ExecStart=/opt/raccoon-dnsrelay/Raccoon.DnsRelay
Restart=on-failure
# ポート 53 を非 root で使う場合
AmbientCapabilities=CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
```

## ライセンス

MIT
