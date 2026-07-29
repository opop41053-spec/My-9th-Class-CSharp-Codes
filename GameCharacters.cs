using System;

class Character
{
    public string name;
    public int health;
    public int Damage;
    
    public void ShowInfo()
    {
        Console.WriteLine("Character Name : " + name + " | Character health : " + health);
    }
    
    public virtual void Attack()
    {
        Console.WriteLine("Attack and Damage");
    }
}

class Player : Character
{
    public int Coins;
    
    public override void Attack()
    {
        Console.WriteLine("Player Gun Attack");
    }
}

class Enemy : Character
{
    public override void Attack()
    {
        Console.WriteLine("Enemy Claw Attack | Damage: " + Damage);
    }
}

class Program
{
    static void Main(string[] args)
    {
        Character player1 = new Player();
        Character enemy1 = new Enemy();
        
        player1.name = "Rohit";
        player1.health = 100;
        
        enemy1.name = "Enemy";
        enemy1.health = 200;
        enemy1.Damage = 20;
        
        player1.ShowInfo();
        player1.Attack();
        
        Console.WriteLine("-----------------------------");
        
        enemy1.ShowInfo();
        enemy1.Attack();
    }
}
