# Grass-Instancing
 
## Introduction 
Hello! Here is a grass system that I build purely for learning after watching Acerola's video about modern foliage. And also this project is my very first one after diving into learning Unity. For my future job opportunity, I will explain this project in detail :nerd_face::point_up:.

Check out Acerola's Youtube channel: [Modern Foliage Rendering](https://www.youtube.com/watch?v=jw00MbIJcrk)

## GPU Instancing
Normally. we create a game object if we want it to render on screen, but this is very costly and unscalable. Imagine a 100-square-meter field with only 100 blades of grass. Terrible, right? So we have to find a new solution, and it is GPU INSTANCING. 

But what the difference between Game Object method and GPU Instancing method?
- Game Object method: CPU is a smart but slow guy and that's where a game object is created. It fills object with rendering components, then it sends all the necessary data to GPU to render. But there is a major problem with this way of rendering, that is we have to send data to GPU, which is largely unefficient, all over again for each object, even they are simillar. GPU is stupidly fast with them math, rendering instructions. It has to wait and remains idle until CPU send the next instruction. Because GPU is not performing at peak, this can lead to fewer FPS. This phenomenon is called Bottle Neck.
- GPU Instancing method: Instead of letting CPU intruct GPU every object, we can let it do only one. Million blades of grass are simillar, the basic idea that the necessary data only sent once and stayed in GPU, and GPU will do all the rendering jobs. We can achieve this through Graphics.DrawMeshInstancedIndirect function.

## Compute Shader
We almost there! The only thing left is to calculate world position for every grass. It's gonna be easy, we can just toss million of calculations in CPU, right? Nuh uh, doing this way gonna be mad inefficient and your Unity will be dead mid way of the process. So what to do now :sob: ? Did I tell you that GPU is very good at doing math instruction, right ? Doing math, running on CPU... tadaaa! [Compute Shader](https://docs.unity3d.com/Manual/class-ComputeShader.html) is the one we need :exploding_head:. It can dispatch thread groups containing threads to do parallel computation. 

Now we implement it. With my dumb shader knowledge, I implemented a basic sine wave wind system, make it colored and drooped. 

[Implementation](/Assets/Grass/Image/Grass.png)




