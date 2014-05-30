namespace FsDroneIOS

open System
open System.Drawing

open MonoTouch.Foundation
open MonoTouch.UIKit

[<Register ("FsDroneIOSViewController")>]
type FsDroneIOSViewController () =
    inherit UIViewController ()

    override x.DidReceiveMemoryWarning () =
        // Releases the view if it doesn't have a superview.
        base.DidReceiveMemoryWarning ()
        // Release any cached data, images, etc that aren't in use.

    override x.ViewDidLoad () =
        base.ViewDidLoad ()
        // Perform any additional setup after loading the view, typically from a nib.
        let btn = new UIButton(RectangleF(10.f,10.f,100.f,100.f))
        btn.SetTitle("Test", UIControlState.Normal)
        x.View.Add(btn)
        let d = FsDrone.Default
        btn.AllEvents.Add(fun _-> btn.SetTitle("clicked", UIControlState.Normal))

    override x.ShouldAutorotateToInterfaceOrientation (toInterfaceOrientation) =
        // Return true for supported orientations
        if UIDevice.CurrentDevice.UserInterfaceIdiom = UIUserInterfaceIdiom.Phone then
           toInterfaceOrientation <> UIInterfaceOrientation.PortraitUpsideDown
        else
           true

