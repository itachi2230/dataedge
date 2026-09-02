<?php

namespace App\Service;

use App\Entity\User;
use App\Repository\AIChatMessageRepository;
use Symfony\Contracts\HttpClient\HttpClientInterface;

class GeminiService
{
    private HttpClientInterface $httpClient;
    private AIChatMessageRepository $messageRepository;
    private string $apiKey;

    public function __construct(
        HttpClientInterface $httpClient,
        AIChatMessageRepository $messageRepository,
        string $geminiApiKey
    ) {
        $this->httpClient = $httpClient;
        $this->messageRepository = $messageRepository;
        $this->apiKey = $geminiApiKey;
    }

    /**
     * Génère la structure de la requête avec l'historique et les instructions système
     */
    private function preparePayload(User $user, string $userPrompt): array
    {
        $history = $this->messageRepository->findChatHistory($user, 20);

        $contents = [];
        foreach ($history as $message) {
            $contents[] = [
                'role' => $message->getRole() === 'model' ? 'model' : 'user',
                'parts' => [
                    ['text' => $message->getContent()]
                ]
            ];
        }

        $contents[] = [
            'role' => 'user',
            'parts' => [
                ['text' => $userPrompt]
            ]
        ];

        $systemInstruction = [
            'parts' => [
                [
                    'text' => "# SYSTEM INSTRUCTIONS - DATAEDGE AI ASSISTANT\n\n" .
                              "## 1. IDENTITÉ ET RÔLE\n" .
                              "Tu es \"DataEdge AI\", un agent d'intelligence artificielle nativement intégré au logiciel DataEdge. Tu ne parles pas en tant qu'entité externe, mais comme un membre expert de l'équipe technique et financière de DataEdge. Tu maîtrises l'utilisation du logiciel DataEdge, le trading Forex/CFD, et l'architecture technique du projet.\n\n" .
                              "## 2. EXPERTISE DU LOGICIEL (DATAEDGE & BACKEND)\n" .
                              "- MainWindow (Dashboard) : Centralise indices de sentiment, notes, journal de trades, statistiques, calendrier.\n" .
                              "- Chart Window : Graphique interactif TradingView Lightweight Charts via WebView2.\n" .
                              "- Statistics & Advanced Stats : Calcule Winrate, Profit Factor, Espérance, stats par session.\n" .
                              "- Module Études : Éditeur de texte riche (format .etude).\n" .
                              "- Synchronisation Cloud : Sauvegarde sécurisée sur fxdataedge.com.\n\n" .
                              "## 3. EXPERTISE EN TRADING\n" .
                              "Analyse technique/Price Action (SMC, ICT, supply/demand), Gestion des risques, et Psychologie du trading.\n\n" .
                              "## 4. DIRECTIVES DE COMPORTEMENT\n" .
                              "- Origine des réponses : Tu sais que les données proviennent du fichier JSON local de stratégie.\n" .
                              "- Précision technique : Utilise le jargon exact du trading."
                ]
            ]
        ];

        return [
            'contents' => $contents,
            'systemInstruction' => $systemInstruction,
            'generationConfig' => [
                'temperature' => 0.7,
                'maxOutputTokens' => 1500,
            ]
        ];
    }

    /**
     * Nouvelle méthode pour streamer la réponse en direct
     */
    public function generateStreamResponse(User $user, string $userPrompt, callable $onChunkReceived): void
    {
        $payload = $this->preparePayload($user, $userPrompt);

        // Version streamGenerateContent de l'API Gemini 2.5 Flash
        $url = sprintf(
            'https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:streamGenerateContent?key=%s',
            $this->apiKey
        );

        $ch = curl_init($url);
        curl_setopt($ch, CURLOPT_RETURNTRANSFER, false);
        curl_setopt($ch, CURLOPT_POST, true);
        curl_setopt($ch, CURLOPT_POSTFIELDS, json_encode($payload));
        curl_setopt($ch, CURLOPT_HTTPHEADER, ['Content-Type: application/json']);
        
        // Callback cURL : déclenché à chaque réception de fragment de texte de Google
        curl_setopt($ch, CURLOPT_WRITEFUNCTION, function($ch, $data) use ($onChunkReceived) {
            // Google envoie un tableau JSON progressif. On extrait le texte de chaque chunk reçu.
            // Format typique d'un chunk : [{"candidates": [{"content": {"parts": [{"text": "mot"}]}}]}]
            $cleanData = trim($data, ", \t\n\r\0\x0B[]");
            if (!empty($cleanData)) {
                $decoded = json_decode($cleanData, true);
                if (isset($decoded['candidates'][0]['content']['parts'][0]['text'])) {
                    $text = $decoded['candidates'][0]['content']['parts'][0]['text'];
                    $onChunkReceived($text);
                }
            }
            return strlen($data);
        });

        curl_exec($ch);
        curl_close($ch);
    }
}