# Direct AI services

The desktop app connects directly to one of these 20 services. Each service has
its own API key, saved separately with Windows DPAPI. OpenRouter has been removed.
Existing Gemini credentials continue to work; an old OpenRouter selection returns
to Gemini and requires choosing/configuring a direct service. An old OpenRouter
key is never sent to another company.

| Service | API base URL | Official reference |
| --- | --- | --- |
| Google Gemini | `generativelanguage.googleapis.com/v1beta` | [Models](https://ai.google.dev/api/models) |
| OpenAI (ChatGPT models) | `api.openai.com/v1` | [Vision](https://developers.openai.com/api/docs/guides/images-vision), [models](https://developers.openai.com/api/reference/resources/models/methods/list) |
| Anthropic Claude | `api.anthropic.com/v1` | [Vision](https://platform.claude.com/docs/en/build-with-claude/vision), [models](https://platform.claude.com/docs/en/api/models/list) |
| xAI Grok | `api.x.ai/v1` | [Language models and modalities](https://docs.x.ai/developers/rest-api-reference/inference/models) |
| Mistral AI | `api.mistral.ai/v1` | [Models](https://docs.mistral.ai/api/endpoint/models), [vision](https://docs.mistral.ai/studio/conversations/vision) |
| Cohere | `api.cohere.com/v2` | [Image inputs](https://docs.cohere.com/docs/image-inputs), [models](https://docs.cohere.com/v2/reference/list-models) |
| Alibaba Qwen, International | `dashscope-intl.aliyuncs.com/compatible-mode/v1` | [Compatibility and regional endpoints](https://www.alibabacloud.com/help/en/model-studio/compatibility-of-openai-with-dashscope) |
| Z.ai GLM | `api.z.ai/api/paas/v4` | [HTTP API](https://docs.z.ai/guides/develop/http/introduction), [vision models](https://docs.z.ai/guides/vlm/glm-5.3-flash) |
| Moonshot Kimi | `api.moonshot.ai/v1` | [Vision inputs](https://platform.kimi.ai/docs/guide/use-kimi-vision-model) |
| StepFun | `api.stepfun.com/v1` | [Platform](https://platform.stepfun.com/), [vision examples](https://platform.stepfun.com/cookbook/step-5-vision-structured) |
| Together AI | `api.together.xyz/v1` | [Available models](https://docs.together.ai/docs/serverless/models) |
| Fireworks AI | `api.fireworks.ai/inference/v1` | [Vision catalog](https://fireworks.ai/models?modelTypes=Vision&serverless=true) |
| Groq | `api.groq.com/openai/v1` | [Vision](https://console.groq.com/docs/vision), [models](https://console.groq.com/docs/models) |
| NVIDIA NIM | `integrate.api.nvidia.com/v1` | [Multimodal models](https://docs.api.nvidia.com/nim/reference/models-1) |
| SambaNova | `api.sambanova.ai/v1` | [Models and deployment modalities](https://docs.sambanova.ai/docs/en/models/sambacloud-models), [vision](https://docs.sambanova.ai/docs/en/features/vision) |
| Nebius Token Factory | `api.tokenfactory.nebius.com/v1` | [Quickstart](https://docs.tokenfactory.nebius.com/quickstart) |
| DeepInfra | `api.deepinfra.com/v1/openai` | [Chat API](https://docs.deepinfra.com/chat/overview) |
| SiliconFlow, International | `api.siliconflow.com/v1` | [Models](https://docs.siliconflow.com/en/api-reference/models/get-model-list) |
| Novita AI | `api.novita.ai/v3/openai` | [Models](https://docs.novita.ai/api-reference/model-apis-llm-list-models) |
| Hyperbolic | `api.hyperbolic.ai/inference/v1` | [Chat API and models](https://www.hyperbolic.ai/docs/inference/text-apis) |

The model list is requested from the chosen service using its key. Some services
publish a global catalog rather than an account-specific access list. A model's
appearance therefore does not guarantee account credit, regional availability,
activation, or remaining quota. Qwen and SiliconFlow entries use their international
endpoints; keys from another region may fail. Chat-app subscriptions and API billing
are separate.

The list accepts image-understanding models using explicit modality/capability
metadata where available; otherwise it uses conservative, documented vision-family
rules. Explicit text-only metadata takes precedence. Unknown model families are
omitted, and an empty result is explained in the interface. Model IDs cannot be
entered manually. Pricing is shown only when provided in the catalog; otherwise the
label is account-dependent. Loading models never sends an image or makes a paid
generation request.

OpenAI and Grok use Responses; Claude uses Messages; Cohere uses v2 Chat; other
services use their OpenAI-compatible Chat Completions interface. Gemini keeps its
native structured-output path. Error assistance and window translation use the
same saved service selection. A provider switch cancels loading and discards obsolete
responses. It updates existing controls without rebuilding/reparenting the dialog.

## Verification

The 0.4.7 crash was reproduced by selecting OpenRouter in the assistant setup:
WPF rejected attaching an existing logical child to a second parent. The regression
test now switches through all 20 services repeatedly in the actual main window and
assistant dialog. Fake-HTTP tests cover each model-list request, direct image request
format, authentication, response parsing, pricing, excluded text-only models, isolated
secrets, connection failures and obsolete responses. No live customer key or paid
generation call is part of the test suite. Real authenticated inference remains
dependent on the user's service account.
