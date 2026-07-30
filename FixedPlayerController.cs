/*
2D Space Shooter
 
Code by Alberto Betella (alberto@betella.net)

Relased Under a Creative Commons License:
Attribution-NonCommercial-ShareAlike 4.0 International (CC BY-NC-SA 4.0)
http://creativecommons.org/licenses/by-nc-sa/4.0/
*/

// SIMPLIFIED VERSION

using UnityEngine;
using System.Collections;


public class playerController : MonoBehaviour {

	public GameObject playerBullet;
	public bool playerIsImmortal = false; // Here you can cheat ;)
	public int playerLives = 3; // read by GUI script
	public bool isGameOver = false; // read by GUI script

	// Tuning
	private float pushUpForce = 6.0f; // force applied when fly button is tapped
	private float playerBulletXOffset = 0.5f;
	private float playerBulletYOffset = 0f;
	private float timeBetweenShots = 0.2f;  // 0.2 = 5 shots per second
	private float timestamp;

	void FixedUpdate () {


		if (Input.GetButton("Fire1"))
		{
			Fire1Pressed ();

		}
		if (Input.GetButton("Fire2"))
		{
			Fire2Pressed ();
		}
	}

	void Fire1Pressed () {
		//Fly up
		GetComponent<Rigidbody2D>().AddForce(new Vector2(0, pushUpForce));

	}

	void Fire2Pressed () {

		if (Time.time >= timestamp) {
			// Shoot bullet
			Instantiate(playerBullet, transform.position+new Vector3 (playerBulletXOffset,playerBulletYOffset,0), Quaternion.identity);
			timestamp = Time.time + timeBetweenShots;
		}
	}


	// NB. Player and its bullet go in two dedicated layers where collisions must be disabled (Edit->project settings->physics 2D)
	// Collision with Trigger is for the bullets and enemies
	void OnTriggerEnter2D(Collider2D thisObject)
	{
		playerDidCollide ();
	}

	// Normal Collision (no trigger) is for floor and ceiling, so we have physical constraints
	void OnCollisionEnter2D(Collision2D thisObject)
	{
		playerDidCollide ();
	}



	// एकदम सही और नया कोड!
void playerDidCollide () {
         // अगर प्लेयर अमर नहीं है, तो तुरंत 1 लाइफ कम करो
         if (!playerIsImmortal) {
         playerLives = playerLives - 1; 
        }

       // लाइफ कम करने के तुरंत बाद चेक करो कि क्या जिंदगी खत्म हो गई?
         if (playerLives <= 0 && !playerIsImmortal) {
         gameOver(); // तुरंत गेम ओवर करो, बिना किसी देरी के!
       }
    }


	void gameOver () {

		//Debug.Log("GAME OVER");
		isGameOver = true; // This is picked by the Update function in GUI.cs
		Time.timeScale = 0.0F; // Stop the game

	}

	// NB. Update is not affected by Time.timeScale (i.e. it also works during Game Over)
	void Update () {

		// NB Input.GetMouseButtonDown(0) not only triggers on the left mouse click, but also iOS & Android touch events 
		if (isGameOver && Input.GetButtonDown("Fire1") || isGameOver && Input.GetMouseButtonDown(0)) {
			//Debug.Log("GAME RESTART");
			Time.timeScale = 1.0F; // Restart the time
			Application.LoadLevel(Application.loadedLevel); // Reload this level
		}

	}


}
