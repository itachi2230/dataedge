<?php

namespace App\Repository;

use App\Entity\AIChatMessage;
use App\Entity\User;
use Doctrine\Bundle\DoctrineBundle\Repository\ServiceEntityRepository;
use Doctrine\Persistence\ManagerRegistry;

/**
 * @extends ServiceEntityRepository<AIChatMessage>
 */
class AIChatMessageRepository extends ServiceEntityRepository
{
    public function __construct(ManagerRegistry $registry)
    {
        parent::__construct($registry, AIChatMessage::class);
    }

    /**
     * Récupère l'historique des X derniers messages d'un utilisateur, trié du plus vieux au plus récent
     */
    public function findChatHistory(User $user, int $limit = 30): array
    {
        $messages = $this->createQueryBuilder('m')
            ->andWhere('m.user = :user')
            ->setParameter('user', $user)
            ->orderBy('m.createdAt', 'DESC')
            ->setMaxResults($limit)
            ->getQuery()
            ->getResult();

        // On remet dans l'ordre chronologique pour l'API Gemini (du plus vieux au plus récent)
        return array_reverse($messages);
    }
}