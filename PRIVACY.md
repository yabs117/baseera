# Baseera Privacy Policy

**Effective Date: November 2026**

This policy covers the Baseera Windows application and the optional Baseera Translate browser extension.

## 1. No Built-in Telemetry or Tracking

Baseera contains no built-in analytics, advertising trackers, background crash-report uploads, or usage reporting to the project. Some features make network requests as described below. A destination can receive ordinary connection information, such as the IP address it sees and request timing.

## 2. Local Data Storage

- **Settings and files.** Desktop data is normally stored in `%LOCALAPPDATA%\Baseera`. An environment override can change the folder. Settings are stored in `settings.json` and may include tool preferences, a configured scan path, model selections, selected WinGet package IDs, or custom release sources. Baseera keeps update logs and recovery information locally.
- **Credentials and saved work.** Windows Data Protection API (DPAPI) protects saved AI and VirusTotal API keys, ChatGPT account records and session tokens, Smart Repair case files, saved investigation history, and the local document search index. ChatGPT account records can include a name, email address, and account identifiers returned by OpenAI. DPAPI protection applies to these records. Settings, logs, and exports are separate files.
- **Offline tools.** File inspection, including local hashing, image conversion, PDF creation, and local document search work on the device without uploading the input files. Local diagnostics can be reviewed without sending them to an AI provider. The document search index can contain text extracted from folders you select. Assistant chats live in memory. You can export a text record, and a saved Smart Repair case stores case details locally. Exports are written to a location you choose. Review exported reports before sharing them.
- **Local controls.** You can sign out of a saved ChatGPT account in the app. To clear the desktop app's local data, close Baseera and delete its data folder. This removes saved settings, credentials, cases, and local history stored there. Manage exports separately wherever you saved them. Removing local data does not remove data already received by a service.

## 3. Data Shared with Third-Party Services

- **AI service requests.** Baseera connects directly to the AI service you configure using that service's API key or, when you choose ChatGPT sign-in, an OpenAI account token. Loading models sends an authenticated catalog request. The optional connection test sends a fixed prompt and a generated 64 × 64 color image. It does not include your chat, files, or diagnostics. ChatGPT sign-in opens OpenAI's sign-in page in your browser. The app exchanges the authorization code with OpenAI and can refresh an authorized session when needed. See the [supported AI services and endpoints](docs/AI-SERVICES.md) and [ChatGPT sign-in documentation](docs/CHATGPT-SIGN-IN.md).
- **Translation and troubleshooting.** Requesting window translation sends an image of the selected window and the target language to your configured service. Sending an Error Assistant message can include the text, a selected image or video, and recent conversation text for context. Gemini can receive the selected video, including audio or metadata present in the clip. Other supported video flows send sampled still frames. Sharing collected diagnostic evidence requires review and approval for that specific request. Recorded audio stays in the app until you approve transcription through a supported API. Windows voice typing is a separate Microsoft feature and follows your [Windows speech and privacy settings](https://support.microsoft.com/en-gb/windows/privacy/speech-voice-activation-inking-typing-and-privacy).
- **File reputation.** When you approve a lookup for a selected file in Security Center, Baseera sends the file's SHA-256 hash to VirusTotal and authenticates with your VirusTotal API key. The file, file name, and path are not sent in the lookup. The hash may identify a known file to the service. [VirusTotal Privacy Policy](https://www.virustotal.com/gui/privacy-policy)
- **Baseera updates.** The desktop app checks the project's public GitHub release feed at startup and periodically while running. Automatic checks are enabled by default and can be disabled in **Settings → Baseera updates**. You can check manually. Requests identify the Baseera version in the HTTP user agent. If you approve the update, the app downloads the executable from GitHub's release infrastructure. Version selection and download verification happen locally. [GitHub General Privacy Statement](https://docs.github.com/en/site-policy/privacy-policies/github-general-privacy-statement)
- **Updates for other apps.** When you use app discovery, Baseera invokes Microsoft WinGet to match installed apps against its `winget` source. WinGet creates a temporary local export of package IDs and versions that Baseera reads and removes after discovery. Baseera does not upload that export. WinGet can contact its package source and, when updating packages you select, their download hosts. WinGet's network and diagnostics behavior depends on its own settings. A custom GitHub release source is checked by requesting release metadata for the repository you configured. [WinGet settings](https://learn.microsoft.com/en-us/windows/package-manager/winget/settings) · [Microsoft Privacy Statement](https://www.microsoft.com/en-us/privacy/privacystatement)
- **Network and repair tools.** When you approve a Network Doctor measurement, that tool uses the system DNS resolver for the host you enter and sends ICMP echo requests to the resolved address. For a literal IP address, it skips DNS. Windows repair commands run only after approval. DISM can contact Windows Update for repair files depending on your Windows configuration. [Windows repair sources](https://learn.microsoft.com/en-us/windows-hardware/manufacture/desktop/configure-a-windows-repair-source?view=windows-11)

Your provider's privacy notice and the terms for your account or API plan govern its handling of requests. Baseera does not control the external service's retention or use of content.

<details>
<summary>AI provider privacy notices and data resources</summary>

| Provider | Policy or data resource |
| --- | --- |
| Google Gemini | [Gemini API terms and data use](https://ai.google.dev/gemini-api/terms) |
| OpenAI and ChatGPT | [OpenAI Privacy Policy](https://openai.com/policies/privacy-policy/) and [API data controls](https://developers.openai.com/api/docs/guides/your-data) |
| Anthropic Claude | [Anthropic Legal Center](https://www.anthropic.com/legal) |
| xAI Grok | [Privacy Policy](https://x.ai/legal/privacy-policy) and [API security FAQ](https://docs.x.ai/developers/faq/security) |
| Mistral AI | [Privacy Policy](https://legal.mistral.ai/terms/privacy-policy/) |
| Cohere | [Privacy Policy](https://cohere.com/privacy) |
| Alibaba Qwen (International) | [Model Studio privacy notice](https://www.alibabacloud.com/help/en/model-studio/privacy-notice.md) |
| Z.ai GLM | [Z.ai public privacy notice](https://chat.z.ai/legal-agreement/privacy-policy) and [API platform](https://z.ai/model-api) |
| Moonshot Kimi | [Kimi OpenPlatform Privacy Policy](https://platform.kimi.ai/docs/agreement/userprivacy) |
| StepFun | [Open Platform Privacy Policy](https://platform.stepfun.com/legal/privacy-policy.html) |
| Together AI | [Privacy Policy](https://www.together.ai/privacy) |
| Fireworks AI | [Privacy Policy](https://fireworks.ai/privacy-policy) |
| Groq | [Privacy Policy](https://groq.com/privacy-policy) and [GroqCloud policies](https://console.groq.com/docs/legal) |
| NVIDIA NIM | [NVIDIA Privacy Policy](https://www.nvidia.com/en-us/about-nvidia/privacy-policy/) |
| SambaNova | [Privacy Policy](https://sambanova.ai/privacy-policy) |
| Nebius Token Factory | [Nebius Privacy Policy](https://docs.nebius.com/legal/privacy) and [Token Factory subprocessors](https://docs.tokenfactory.nebius.com/legal/subprocessors) |
| DeepInfra | [Privacy Policy](https://deepinfra.com/privacy) |
| SiliconFlow (International) | [Privacy Policy](https://docs.siliconflow.com/en/legals/privacy-policy) |
| Novita AI | [Privacy Policy](https://novita.ai/legal/privacy-policy) |
| Hyperbolic | [Privacy Policy](https://www.hyperbolic.ai/privacy) |

</details>

## 4. In-App Feedback

When you select **Send feedback**, the app sends a UTF-8 JSON request to the project's Cloudflare Workers endpoint at `https://baseera-feedback.yabsganes.workers.dev/`. The request body contains the selected feedback category (`type`), the title (`title`), and the details (`details`). Baseera trims the title and details and requires both before sending. The app does not attach files, screenshots, credentials, an email address, or diagnostic reports. Any information you type into the form is included.

The draft stays in memory. A successful HTTP response clears the title and details. If delivery cannot be confirmed, the draft stays available for retry. The public source contains the feedback client. It does not specify the server's retention, logging, or downstream handling. Contact the project with questions about a submission. [Feedback source code](src/Baseera.App/MainWindow.Feedback.cs) · [Cloudflare Privacy Policy](https://www.cloudflare.com/privacypolicy/)

## 5. Browser Extension

Baseera Translate uses the browser's temporary active-tab permission to read text from the current HTTP or HTTPS page when you select **Translate**. It sends eligible visible text to Google's Gemini API in batches using your own key, model, and target language. One run is limited to 300 text segments and 30,000 characters. The extension skips input fields, editable elements, code blocks, and hidden text. It does not send images, iframe contents, or a separate page URL field. Text itself can contain links or personal information.

The API key is separate from the desktop app and stored in browser session storage. **Forget key** removes that session copy. The model, target language, and interface language are stored locally by the extension. The extension keeps original text in the tab for restoration. **Restore original** works in the tab without a Gemini request and cannot recall text already sent to Google. See the [extension source](extension) and Google's [Gemini API data terms](https://ai.google.dev/gemini-api/terms).

## 6. Contact & Source Code

For privacy questions or questions about an in-app feedback submission, contact the maintainers through the [Baseera GitHub repository](https://github.com/yabs117/baseera). GitHub issues are public. Please keep private details out of public posts.

Baseera's source is available under the [MIT License](LICENSE).
