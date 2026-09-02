<?php

namespace App\Controller;

use App\Entity\AIChatMessage;
use App\Entity\User;
use App\Service\GeminiService;
use Doctrine\ORM\EntityManagerInterface;
use Symfony\Bundle\FrameworkBundle\Controller\AbstractController;
use Symfony\Component\HttpFoundation\Request;
use Symfony\Component\HttpFoundation\Response;
use Symfony\Component\HttpFoundation\StreamedResponse;
use Symfony\Component\Routing\Annotation\Route;

#[Route('/api/ai')]
class AIChatController extends AbstractController
{
    private GeminiService $geminiService;
    private EntityManagerInterface $em;

    public function __construct(
        GeminiService $geminiService,
        EntityManagerInterface $em
    ) {
        $this->geminiService = $geminiService;
        $this->em = $em;
    }

    #[Route('/chat', name: 'api_ai_chat', methods: ['POST'])]
    public function chat(Request $request): Response
    {
        /** @var User $user */
        $user = $this->getUser();
        if (!$user) {
            return new Response('Not authenticated', 401);
        }

        $data = json_decode($request->getContent(), true);
        $prompt = $data['message'] ?? null;

        if (!$prompt || trim($prompt) === '') {
            return new Response('Message cannot be empty', 400);
        }

        // 1. Sauvegarder la question de l'utilisateur en base de données
        $userMessage = new AIChatMessage();
        $userMessage->setUser($user);
        $userMessage->setRole('user');
        $userMessage->setContent($prompt);
        $this->em->persist($userMessage);
        $this->em->flush();

        // 2. Préparer une réponse streamée
        $response = new StreamedResponse(function () use ($user, $prompt) {
            $fullAiResponse = "";

            // On appelle le service Gemini en lui passant un callback de traitement
            $this->geminiService->generateStreamResponse($user, $prompt, function($chunk) use (&$fullAiResponse) {
                // On accumule pour la sauvegarde finale en BDD
                $fullAiResponse .= $chunk;

                // On envoie immédiatement le morceau au client C# WPF avec un retour à la ligne
                echo $chunk . "\n";
                ob_flush();
                flush();
            });

            // Une fois le stream terminé, on sauvegarde la réponse complète de l'IA en BDD
            $aiMessage = new AIChatMessage();
            $aiMessage->setUser($user);
            $aiMessage->setRole('model');
            $aiMessage->setContent($fullAiResponse);
            $this->em->persist($aiMessage);
            $this->em->flush();
        });

        // Entêtes nécessaires pour le streaming HTTP
        $response->headers->set('Content-Type', 'text/event-stream');
        $response->headers->set('Cache-Control', 'no-cache');
        $response->headers->set('Connection', 'keep-alive');
        $response->headers->set('X-Accel-Buffering', 'no'); // Désactive le cache proxy (Nginx) pour un streaming instantané

        return $response;
    }
}